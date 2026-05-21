/**
 * ChatComponent - manages the chat container DOM element and applies layout changes.
 */
class ChatComponent {
    constructor() {
        this.chatContainer = null;
    }

    setup() {
        this.chatContainer = document.getElementById('chat-container');
    }

    reset() {
        this.chatContainer = null;
    }

    update(state, prev) {
        if (
            prev &&
            state.EnableCodeCollapse === prev.EnableCodeCollapse
        ) {
            return;
        }

        this.chatContainer?.classList.toggle('layout-collapsed-code', !!state.EnableCodeCollapse);

    }
}

const chatComponent = new ChatComponent();
export { chatComponent };