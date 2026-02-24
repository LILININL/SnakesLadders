(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, formatClock } = root.utils;

  function renderChat() {
    const messages = Array.isArray(state.chatMessages) ? state.chatMessages : [];
    if (messages.length === 0) {
      el.chatList.innerHTML = "<li class='chat-item'><div class='meta'>ยังไม่มีข้อความแชต</div></li>";
      return;
    }

    const rows = messages.map((msg) => {
      const isSelf = msg.playerId === state.playerId;
      return `
        <li class="chat-item${isSelf ? " self" : ""}">
          <div class="meta">${escapeHtml(msg.displayName)} • ${formatClock(msg.sentAtUtc)}</div>
          <div>${escapeHtml(msg.message)}</div>
        </li>
      `;
    });

    el.chatList.innerHTML = rows.join("");
    el.chatList.scrollTop = el.chatList.scrollHeight;
  }

  function clearChat() {
    state.chatMessages = [];
    renderChat();
  }

  function addChatMessage(message) {
    if (!message?.messageId) {
      return;
    }

    if (state.chatMessages.some((x) => x.messageId === message.messageId)) {
      return;
    }

    state.chatMessages.push(message);
    if (state.chatMessages.length > 120) {
      state.chatMessages = state.chatMessages.slice(-120);
    }

    renderChat();
  }

  root.renderChat = {
    renderChat,
    clearChat,
    addChatMessage
  };
})();
