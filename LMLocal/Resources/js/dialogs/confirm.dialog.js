export class ConfirmDialog {
    async confirm(hasModel = false) {
        const dialog = document.getElementById('confirm-dialog');
        if (!dialog) throw new Error('Dialog #confirm-dialog not found');

        const confirmBtn = dialog.querySelector('#confirm-dialog-confirm');
        const cancelBtn = dialog.querySelector('#confirm-dialog-cancel');
        const summarizeRadio = dialog.querySelector('#confirm-action-summarize');

        if (!confirmBtn || !cancelBtn) {
            throw new Error('Missing #confirm-dialog-confirm or #confirm-dialog-cancel');
        }

        if (summarizeRadio) {
            summarizeRadio.disabled = !hasModel;
        }

        return new Promise((resolve) => {
            dialog.showModal();

            const getSelectedAction = () => {
                const selectedRadio = dialog.querySelector('input[name="clear-action"]:checked');
                return selectedRadio ? selectedRadio.id.replace('confirm-action-', '') : 'none';
            };

            const onConfirm = () => {
                const action = getSelectedAction();
                dialog.close();
                resolve({ confirmed: true, action });
            };

            const onCancel = () => {
                dialog.close();
                resolve({ confirmed: false, action: null });
            };

            const onClose = () => {
                resolve({ confirmed: false, action: null });
            };

            confirmBtn.onclick = onConfirm;
            cancelBtn.onclick = onCancel;
            dialog.onclose = onClose;
        });
    }
}