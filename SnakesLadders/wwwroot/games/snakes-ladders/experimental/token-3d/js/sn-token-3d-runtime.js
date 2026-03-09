const root = (window.SNL ??= {});

root.experimentalToken3d ??= {
  shouldUseForPlayer: () => false,
  mountToken: () => false,
  unmountToken: () => {},
  mountAvatarHost: () => false,
  unmountAvatarHost: () => {},
  hydrateAvatarHosts: () => {},
  resolveProfile: () => null,
};

(async () => {
  let THREE;
  let GLTFLoader;

  try {
    ({ ...THREE } = await import("three"));
    ({ GLTFLoader } = await import("three/addons/loaders/GLTFLoader.js"));
  } catch (error) {
    console.warn("[token-3d] Failed to load Three.js GLTF runtime. Falling back to 2D avatars.", error);
    return;
  }

  const PROFILE_BY_AVATAR_ID = new Map([
    [9, {
      key: "wakamo-swimsuit",
      label: "Wakamo Swimsuit",
      glbUrl: "/games/snakes-ladders/experimental/token-3d/assets/blue_archive_-wakamo_swimsuit-.glb",
      idleBob: 0.052,
      idleTiltX: -0.16,
      idleYaw: 0.08,
      idleRoll: 0.026,
      walkBounce: 0.19,
      walkSway: 0.11,
      walkLean: 0.13,
      walkStrideSpeed: 8.6,
      cameraFov: 21,
      floorLift: 0,
      boardScale: 1.28,
      transitScale: 1.42,
      lightIntensity: 1.5,
    }],
  ]);

  const viewsByToken = new WeakMap();
  const viewsByAvatarHost = new WeakMap();
  const activeViews = new Set();
  const loadCache = new Map();
  const deadTokenGraceMs = 1600;
  let animationFrameId = 0;

  function shouldUseForPlayer(player) {
    return Boolean(resolveProfile(player));
  }

  function mountToken(token, player, options = {}) {
    const profile = resolveProfile(player);
    if (!profile || !token) {
      return false;
    }

    let view = viewsByToken.get(token);
    const transit = Boolean(options.transit);
    if (!view || view.profile.key !== profile.key || view.transit !== transit) {
      if (view) {
        disposeView(view);
      }
      view = createView(token, profile, transit);
      viewsByToken.set(token, view);
    }

    token.classList.add("token-3d-active");
    token.dataset.token3dKey = profile.key;
    token.dataset.token3dTransit = transit ? "1" : "0";
    token.title = player?.displayName ?? profile.label;
    view.lastSeenAt = Date.now();
    view.playerId = String(player?.playerId ?? "");

    if (!view.modelReady && !view.loading) {
      view.loading = true;
      ensureAssetBundle(profile)
        .then((bundle) => {
          if (!viewsByToken.has(token)) {
            return;
          }
          attachBundleToView(view, bundle);
        })
        .catch((error) => {
          console.error("[token-3d] failed to load 3D token", error);
          token.classList.add("token-3d-error");
        })
        .finally(() => {
          view.loading = false;
        });
    }

    ensureAnimationLoop();
    return true;
  }

  function unmountToken(token) {
    if (!token) {
      return;
    }

    const view = viewsByToken.get(token);
    if (view) {
      disposeView(view);
      viewsByToken.delete(token);
    }

    token.classList.remove(
      "token-3d-active",
      "token-3d-ready",
      "token-3d-loading",
      "token-3d-error",
    );
    delete token.dataset.token3dKey;
    delete token.dataset.token3dTransit;
  }

  function mountAvatarHost(host, avatar, options = {}) {
    const profile = resolveProfile(avatar);
    if (!profile || !host) {
      return false;
    }

    const variant = String(options.variant ?? "inline").trim() || "inline";
    let view = viewsByAvatarHost.get(host);
    if (!view || view.profile.key !== profile.key || view.variant !== variant) {
      if (view) {
        disposeView(view);
      }
      view = createAvatarHostView(host, profile, variant);
      viewsByAvatarHost.set(host, view);
    }

    host.dataset.avatarModelId = String(
      Number.parseInt(String(avatar?.avatarId ?? avatar ?? 0), 10) || 0,
    );
    host.dataset.avatarModelVariant = variant;
    view.lastSeenAt = Date.now();

    if (!view.modelReady && !view.loading) {
      view.loading = true;
      ensureAssetBundle(profile)
        .then((bundle) => {
          if (!viewsByAvatarHost.has(host)) {
            return;
          }
          attachBundleToView(view, bundle);
        })
        .catch((error) => {
          console.error("[token-3d] failed to load 3D avatar host", error);
          renderAvatarFallback(host, avatar, variant);
        })
        .finally(() => {
          view.loading = false;
        });
    }

    ensureAnimationLoop();
    return true;
  }

  function unmountAvatarHost(host) {
    if (!host) {
      return;
    }

    const view = viewsByAvatarHost.get(host);
    if (view) {
      disposeView(view);
      viewsByAvatarHost.delete(host);
    }

    host.classList.remove(
      "avatar-model-preview-active",
      "token-3d-loading",
      "token-3d-ready",
      "token-3d-error",
    );
    delete host.dataset.avatarModelId;
    delete host.dataset.avatarModelVariant;
  }

  function hydrateAvatarHosts(scope = document) {
    const hosts = scope?.querySelectorAll?.("[data-avatar-model-id]") ?? [];
    for (const host of hosts) {
      const avatarId =
        Number.parseInt(String(host.dataset.avatarModelId ?? 0), 10) || 0;
      const variant = String(host.dataset.avatarModelVariant ?? "inline");
      if (!mountAvatarHost(host, { avatarId }, { variant })) {
        renderAvatarFallback(host, { avatarId }, variant);
      }
    }
  }

  function resolveProfile(player) {
    const avatarId = Number.parseInt(String(player?.avatarId ?? ""), 10) || 0;
    return PROFILE_BY_AVATAR_ID.get(avatarId) ?? null;
  }

  function createView(token, profile, transit) {
    token.replaceChildren();
    token.classList.add("token-3d-loading");
    token.classList.remove("avatar");

    const canvas = document.createElement("canvas");
    canvas.className = "token-3d-canvas";
    token.appendChild(canvas);

    const renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: true,
      powerPreference: "low-power",
      premultipliedAlpha: true,
    });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.35));
    if ("outputColorSpace" in renderer && THREE.SRGBColorSpace) {
      renderer.outputColorSpace = THREE.SRGBColorSpace;
    }

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(profile.cameraFov, 1, 0.1, 1000);
    const pivot = new THREE.Group();
    scene.add(pivot);

    const hemi = new THREE.HemisphereLight(0xf3fbff, 0x6ea0c2, profile.lightIntensity);
    hemi.position.set(0, 1, 0);
    scene.add(hemi);

    const key = new THREE.DirectionalLight(0xffffff, 1.22);
    key.position.set(1.8, 2.8, 3.2);
    scene.add(key);

    const rim = new THREE.DirectionalLight(0x93d4ff, 0.78);
    rim.position.set(-2.2, 1.5, -2.6);
    scene.add(rim);

    const fill = new THREE.DirectionalLight(0xd5f1ff, 0.5);
    fill.position.set(0, 0.8, 2.6);
    scene.add(fill);

    const view = {
      host: token,
      token,
      canvas,
      renderer,
      scene,
      camera,
      pivot,
      profile,
      transit,
      playerId: "",
      modelRoot: null,
      modelReady: false,
      loading: false,
      width: 0,
      height: 0,
      startedAt: performance.now(),
      lastSeenAt: Date.now(),
      baseRootPosition: null,
      baseRootRotation: null,
    };

    activeViews.add(view);
    return view;
  }

  function createAvatarHostView(host, profile, variant) {
    host.replaceChildren();
    host.classList.add("avatar-model-preview-active", "token-3d-loading");
    host.classList.remove("token-3d-error");

    const canvas = document.createElement("canvas");
    canvas.className = "token-3d-canvas";
    host.appendChild(canvas);

    const renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: true,
      powerPreference: "low-power",
      premultipliedAlpha: true,
    });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.35));
    if ("outputColorSpace" in renderer && THREE.SRGBColorSpace) {
      renderer.outputColorSpace = THREE.SRGBColorSpace;
    }

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(resolvePreviewFov(variant), 1, 0.1, 1000);
    const pivot = new THREE.Group();
    scene.add(pivot);

    const hemi = new THREE.HemisphereLight(0xf3fbff, 0x6ea0c2, profile.lightIntensity);
    hemi.position.set(0, 1, 0);
    scene.add(hemi);

    const key = new THREE.DirectionalLight(0xffffff, 1.18);
    key.position.set(1.8, 2.8, 3.2);
    scene.add(key);

    const rim = new THREE.DirectionalLight(0x93d4ff, 0.74);
    rim.position.set(-2.2, 1.5, -2.6);
    scene.add(rim);

    const fill = new THREE.DirectionalLight(0xd5f1ff, 0.46);
    fill.position.set(0, 0.8, 2.6);
    scene.add(fill);

    const view = {
      host,
      token: null,
      canvas,
      renderer,
      scene,
      camera,
      pivot,
      profile,
      transit: false,
      variant,
      previewScale: resolvePreviewScale(variant),
      previewCameraY: resolvePreviewCameraY(variant),
      previewCameraZ: resolvePreviewCameraZ(variant),
      previewLookAtY: resolvePreviewLookAtY(variant),
      playerId: "",
      modelRoot: null,
      modelReady: false,
      loading: false,
      width: 0,
      height: 0,
      startedAt: performance.now(),
      lastSeenAt: Date.now(),
      baseRootPosition: null,
      baseRootRotation: null,
    };

    activeViews.add(view);
    return view;
  }

  function ensureAssetBundle(profile) {
    if (loadCache.has(profile.key)) {
      return loadCache.get(profile.key);
    }

    const promise = new Promise((resolve, reject) => {
      const loader = new GLTFLoader();
      loader.load(
        profile.glbUrl,
        (gltf) => {
          const object = gltf?.scene;
          if (!object) {
            reject(new Error("GLB scene missing"));
            return;
          }
          normalizeLoadedModel(object);
          resolve({ object });
        },
        undefined,
        reject,
      );
    });

    loadCache.set(profile.key, promise);
    return promise;
  }

  function normalizeLoadedModel(object) {
    object.traverse((child) => {
      if (!child?.isMesh) {
        return;
      }

      child.castShadow = false;
      child.receiveShadow = false;
      const materials = Array.isArray(child.material) ? child.material : [child.material];
      materials.forEach((material) => {
        if (!material) {
          return;
        }
        material.side = THREE.DoubleSide;
        material.needsUpdate = true;
        if ("map" in material && material.map && "colorSpace" in material.map && THREE.SRGBColorSpace) {
          material.map.colorSpace = THREE.SRGBColorSpace;
        }
        if ("transparent" in material) {
          material.transparent = true;
          if ("alphaTest" in material) {
            material.alphaTest = 0.05;
          }
        }
      });
    });
  }

  function attachBundleToView(view, bundle) {
    if (!view?.pivot || !bundle?.object) {
      return;
    }

    if (view.modelRoot) {
      view.pivot.remove(view.modelRoot);
    }

    const modelRoot = bundle.object.clone(true);
    const wrapper = new THREE.Group();
    wrapper.add(modelRoot);
    fitModel(wrapper, view.profile, view.transit, view.previewScale ?? 1);
    view.modelRoot = wrapper;
    view.baseRootPosition = wrapper.position.clone();
    view.baseRootRotation = wrapper.rotation.clone();
    view.pivot.add(wrapper);
    view.modelReady = true;
    view.host.classList.remove("token-3d-loading");
    view.host.classList.add("token-3d-ready");
  }

  function fitModel(wrapper, profile, transit, scaleMultiplier = 1) {
    const box = new THREE.Box3().setFromObject(wrapper);
    const size = box.getSize(new THREE.Vector3());
    const targetHeight = transit ? 2.15 : 1.88;
    const sourceHeight = Math.max(size.y, 0.001);
    const scale =
      (targetHeight / sourceHeight) *
      (transit ? profile.transitScale : profile.boardScale) *
      scaleMultiplier;
    wrapper.scale.setScalar(scale);

    const scaledBox = new THREE.Box3().setFromObject(wrapper);
    const scaledCenter = scaledBox.getCenter(new THREE.Vector3());
    wrapper.position.x -= scaledCenter.x;
    wrapper.position.z -= scaledCenter.z;
    wrapper.position.y -= scaledBox.min.y;
    wrapper.position.y += profile.floorLift;
    wrapper.rotation.y = Math.PI;
    wrapper.rotation.x = profile.idleTiltX;
  }

  function disposeView(view) {
    activeViews.delete(view);
    try {
      if (view.modelRoot) {
        view.pivot.remove(view.modelRoot);
      }
      view.renderer?.dispose?.();
    } catch (error) {
      console.warn("[token-3d] dispose warning", error);
    }
  }

  function ensureAnimationLoop() {
    if (animationFrameId) {
      return;
    }

    const tick = (now) => {
      animationFrameId = 0;
      renderActiveViews(now);
      if (activeViews.size > 0) {
        animationFrameId = window.requestAnimationFrame(tick);
      }
    };

    animationFrameId = window.requestAnimationFrame(tick);
  }

  function renderActiveViews(now) {
    for (const view of Array.from(activeViews)) {
      if (shouldDropView(view)) {
        if (view.token) {
          viewsByToken.delete(view.token);
        }
        if (view.host && !view.token) {
          viewsByAvatarHost.delete(view.host);
        }
        disposeView(view);
        continue;
      }

      syncViewViewport(view);
      if (!view.modelReady || view.width <= 0 || view.height <= 0) {
        continue;
      }

      const elapsed = (now - view.startedAt) / 1000;
      applyProceduralMotion(view, elapsed);
      view.camera.position.set(
        0,
        view.previewCameraY ?? (view.transit ? 2.4 : 1.92),
        view.previewCameraZ ?? (view.transit ? 6.4 : 5.25),
      );
      view.camera.lookAt(
        0,
        view.previewLookAtY ?? (view.transit ? 1.22 : 1.04),
        0,
      );
      view.renderer.render(view.scene, view.camera);
    }
  }

  function applyProceduralMotion(view, elapsed) {
    if (!view.modelRoot || !view.baseRootPosition || !view.baseRootRotation) {
      return;
    }

    const rootPosition = view.baseRootPosition;
    const rootRotation = view.baseRootRotation;

    if (view.transit) {
      const stride = Math.sin(elapsed * view.profile.walkStrideSpeed);
      const bounce = Math.abs(stride);
      const sway = Math.sin(elapsed * view.profile.walkStrideSpeed * 0.5);

      view.pivot.rotation.y = Math.PI + sway * 0.045;
      view.pivot.position.y = bounce * view.profile.walkBounce;

      view.modelRoot.position.set(
        rootPosition.x + stride * 0.08,
        rootPosition.y + bounce * 0.07,
        rootPosition.z,
      );
      view.modelRoot.rotation.x = rootRotation.x + bounce * view.profile.walkLean;
      view.modelRoot.rotation.y = rootRotation.y;
      view.modelRoot.rotation.z = rootRotation.z + stride * view.profile.walkSway;
      return;
    }

    const sway = Math.sin(elapsed * 1.35);
    const breathe = Math.sin(elapsed * 2.1);

    view.pivot.rotation.y = Math.PI + sway * view.profile.idleYaw;
    view.pivot.position.y = breathe * view.profile.idleBob;

    view.modelRoot.position.set(
      rootPosition.x + sway * 0.03,
      rootPosition.y + Math.abs(breathe) * 0.012,
      rootPosition.z,
    );
    view.modelRoot.rotation.x = rootRotation.x + Math.abs(breathe) * 0.035;
    view.modelRoot.rotation.y = rootRotation.y;
    view.modelRoot.rotation.z = rootRotation.z + sway * view.profile.idleRoll;
  }

  function syncViewViewport(view) {
    const rect = view.host.getBoundingClientRect();
    const width = Math.max(12, Math.round(rect.width));
    const height = Math.max(12, Math.round(rect.height));
    if (width === view.width && height === view.height) {
      return;
    }

    view.width = width;
    view.height = height;
    view.renderer.setSize(width, height, false);
    view.camera.aspect = width / height;
    view.camera.updateProjectionMatrix();
  }

  function shouldDropView(view) {
    if (!view?.host) {
      return true;
    }

    if (document.body.contains(view.host)) {
      return false;
    }

    return Date.now() - view.lastSeenAt > deadTokenGraceMs;
  }

  function renderAvatarFallback(host, avatar, variant) {
    const parsedAvatarId =
      Number.parseInt(String(avatar?.avatarId ?? avatar ?? 1), 10) || 1;
    const safeAvatarId =
      root.utils?.normalizeAvatarId?.(avatar?.avatarId ?? avatar, 1) ??
      parsedAvatarId;
    const src = root.utils?.avatarSrc?.(safeAvatarId) ??
      `/games/snakes-ladders/assets/avatars/avatar-${String(safeAvatarId).padStart(2, "0")}.${safeAvatarId === 8 ? "gif" : "png"}`;
    const className =
      variant === "winner" || variant === "first-turn"
        ? `${variant}-avatar-media`
        : variant === "picker"
          ? "avatar-choice-visual"
          : "inline-avatar";

    unmountAvatarHost(host);
    host.innerHTML = `<img class="${className}" src="${src}" alt="Avatar ${safeAvatarId}">`;
  }

  function resolvePreviewScale(variant) {
    switch (variant) {
      case "picker":
        return 1.28;
      case "winner":
        return 1.58;
      case "first-turn":
        return 1.52;
      default:
        return 1.12;
    }
  }

  function resolvePreviewFov(variant) {
    return variant === "inline" ? 18 : 20;
  }

  function resolvePreviewCameraY(variant) {
    switch (variant) {
      case "winner":
      case "first-turn":
        return 2.1;
      case "picker":
        return 2.02;
      default:
        return 1.82;
    }
  }

  function resolvePreviewCameraZ(variant) {
    switch (variant) {
      case "winner":
      case "first-turn":
        return 5.55;
      case "picker":
        return 5.2;
      default:
        return 4.78;
    }
  }

  function resolvePreviewLookAtY(variant) {
    switch (variant) {
      case "winner":
      case "first-turn":
        return 1.08;
      case "picker":
        return 1.04;
      default:
        return 0.96;
    }
  }

  root.experimentalToken3d = {
    shouldUseForPlayer,
    mountToken,
    unmountToken,
    mountAvatarHost,
    unmountAvatarHost,
    hydrateAvatarHosts,
    resolveProfile,
  };
})();
