import { createCallback } from '@app/lib/callback.js';
import { appSelectors } from '@app/store/app.selectors.js';

class ChangesPanelComponent {
    static RESIZE_MIN = 80;
    static RESIZE_MAX = 500;
    constructor() {
        this.panelElement = null;
        this.headerTrigger = null;
        this.filesList = null;
        this.reviewAllBtn = null;
        this.openAllBtn = null;
        this.discardAllBtn = null;
        this.acceptAllBtn = null;
        this.toggleViewModeBtn = null;

        this._isExpanded = false;
        this._viewMode = 'list';// 'list' | 'tree'
        this._cachedFiles = [];
        this._processingCounter = 0;
        this._resizeState = { active: false, startY: 0, startHeight: 0 };
        this._resizerBound = { down: null, move: null, up: null };
        this.onDiscardAll = createCallback();
        this.onAcceptAll = createCallback();
        this.onOpenAll = createCallback();
        this.onOpenFile = createCallback();

        this.onReviewFile = createCallback();
        this.onReviewAll = createCallback();
        this.onDiscardSingleFile = createCallback();
        this.onAcceptSingleFile = createCallback();

        this._togglePanel = this._togglePanel.bind(this);
        this._handleReviewAll = this._handleReviewAll.bind(this);
        this._handleOpenAll = this._handleOpenAll.bind(this);
        this._handleDiscardAll = this._handleDiscardAll.bind(this);
        this._handleAcceptAll = this._handleAcceptAll.bind(this);
        this._toggleViewMode = this._toggleViewMode.bind(this);
        this._handleFileListClick = this._handleFileListClick.bind(this);
    }

    setup() {
        this.panelElement = document.getElementById('global-changes-panel');
        if (!this.panelElement) return;

        this.headerTrigger = this.panelElement.querySelector('#changes-header-trigger');
        this.filesList = this.panelElement.querySelector('#global-files-list');
        this.reviewAllBtn = this.panelElement.querySelector('#review-all-btn');
        this.openAllBtn = this.panelElement.querySelector('#open-all-btn');
        this.discardAllBtn = this.panelElement.querySelector('#discard-all-btn');
        this.acceptAllBtn = this.panelElement.querySelector('#accept-all-btn');
        this.toggleViewModeBtn = this.panelElement.querySelector('#toggle-view-mode-btn');
        if (this.toggleViewModeBtn) {
            this.toggleViewModeBtn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 12H4M4 6h16M4 18h16"/></svg>`;
        }

        this._attachEvents();
        this._initResizer();
        this._updateUiState();
    }

    reset() {
        this._destroyResizer();
        this._detachEvents();
        this.panelElement = null;
        this.headerTrigger = null;
        this.filesList = null;
        this.reviewAllBtn = null;
        this.openAllBtn = null;
        this.discardAllBtn = null;
        this.acceptAllBtn = null;
        this.toggleViewModeBtn = null;

        this.onDiscardAll.off();
        this.onAcceptAll.off();
        this.onOpenAll.off();
        this.onOpenFile.off();
        this.onReviewFile.off();
        this.onReviewAll.off();
        this.onDiscardSingleFile.off();
        this._processingCounter = 0;
    }

    _attachEvents() {
        if (this.headerTrigger) this.headerTrigger.addEventListener('click', this._togglePanel);
        if (this.reviewAllBtn) this.reviewAllBtn.addEventListener('click', this._handleReviewAll);
        if (this.openAllBtn) this.openAllBtn.addEventListener('click', this._handleOpenAll);
        if (this.discardAllBtn) this.discardAllBtn.addEventListener('click', this._handleDiscardAll);
        if (this.acceptAllBtn) this.acceptAllBtn.addEventListener('click', this._handleAcceptAll);
        if (this.toggleViewModeBtn) this.toggleViewModeBtn.addEventListener('click', this._toggleViewMode);
        if (this.filesList) this.filesList.addEventListener('click', this._handleFileListClick);
    }

    _detachEvents() {
        if (this.headerTrigger) this.headerTrigger.removeEventListener('click', this._togglePanel);
        if (this.reviewAllBtn) this.reviewAllBtn.removeEventListener('click', this._handleReviewAll);
        if (this.openAllBtn) this.openAllBtn.removeEventListener('click', this._handleOpenAll);
        if (this.discardAllBtn) this.discardAllBtn.removeEventListener('click', this._handleDiscardAll);
        if (this.acceptAllBtn) this.acceptAllBtn.removeEventListener('click', this._handleAcceptAll);
        if (this.toggleViewModeBtn) this.toggleViewModeBtn.removeEventListener('click', this._toggleViewMode);
        if (this.filesList) this.filesList.removeEventListener('click', this._handleFileListClick);
    }

    _initResizer() {
        const resizer = this.panelElement
            ? this.panelElement.querySelector('#changes-panel-drag-resizer')
            : null;
        if (!resizer) return;
        this._resizerBound.down = this._onResizerMouseDown.bind(this);
        resizer.addEventListener('mousedown', this._resizerBound.down);
    }

    _onResizerMouseDown(e) {
        if (!this._isExpanded) return;
        const body = this.panelElement.querySelector('.changes-body');
        if (!body) return;

        this._resizeState.active = true;
        this._resizeState.startY = e.clientY;
        this._resizeState.startHeight = body.getBoundingClientRect().height;

        const resizer = this.panelElement.querySelector('#changes-panel-drag-resizer');
        if (resizer) resizer.classList.add('is-resizing');
        document.body.style.cursor = 'ns-resize';
        document.body.style.userSelect = 'none';

        this._resizerBound.move = this._onResizerMouseMove.bind(this);
        this._resizerBound.up = this._onResizerMouseUp.bind(this);
        document.addEventListener('mousemove', this._resizerBound.move);
        document.addEventListener('mouseup', this._resizerBound.up);
    }

    _onResizerMouseMove(e) {
        if (!this._resizeState.active) return;

        const deltaY = e.clientY - this._resizeState.startY;
        const newHeight = Math.min(
            ChangesPanelComponent.RESIZE_MAX,
            Math.max(ChangesPanelComponent.RESIZE_MIN, this._resizeState.startHeight - deltaY)
        );

        const body = this.panelElement.querySelector('.changes-body');
        if (body) {
            body.style.setProperty('height', `${newHeight}px`);
            body.style.setProperty('max-height', `${newHeight}px`);
        }
    }

    _onResizerMouseUp() {
        this._resizeState.active = false;

        const resizer = this.panelElement
            ? this.panelElement.querySelector('#changes-panel-drag-resizer')
            : null;
        if (resizer) resizer.classList.remove('is-resizing');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';

        document.removeEventListener('mousemove', this._resizerBound.move);
        document.removeEventListener('mouseup', this._resizerBound.up);
        this._resizerBound.move = null;
        this._resizerBound.up = null;
    }

    _destroyResizer() {
        const resizer = this.panelElement
            ? this.panelElement.querySelector('#changes-panel-drag-resizer')
            : null;
        if (resizer && this._resizerBound.down)
            resizer.removeEventListener('mousedown', this._resizerBound.down);
        if (this._resizerBound.move)
            document.removeEventListener('mousemove', this._resizerBound.move);
        if (this._resizerBound.up)
            document.removeEventListener('mouseup', this._resizerBound.up);
        this._resizerBound = { down: null, move: null, up: null };
    }

    _updateUiState() {
        const isProcessing = this._processingCounter > 0;
        const hasChanges = this._cachedFiles.length > 0;
        const disabled = isProcessing || !hasChanges;

        if (this.reviewAllBtn) this.reviewAllBtn.disabled = disabled;
        if (this.openAllBtn) this.openAllBtn.disabled = disabled;
        if (this.discardAllBtn) this.discardAllBtn.disabled = disabled;
        if (this.acceptAllBtn) this.acceptAllBtn.disabled = disabled;

        if (this.panelElement) {
            this.panelElement.classList.toggle('processing', isProcessing);
        }

        if (this.filesList) {
            this.filesList.style.pointerEvents = isProcessing ? 'none' : 'auto';
            this.filesList.style.opacity = isProcessing ? '0.6' : '1';
        }
    }

    _incrementProcessing() {
        this._processingCounter++;
        this._updateUiState();
    }

    _decrementProcessing() {
        if (this._processingCounter <= 0) {
            this._processingCounter = 0;
        } else {
            this._processingCounter--;
        }

        this._updateUiState();
    }

    updateAppState(state, prev) {
        if (prev && state.status === prev.status) {
            return;
        }

        const currBusy = appSelectors.isBusy(state.status);
        const prevBusy = prev ? appSelectors.isBusy(prev.status) : false;

        if (currBusy && !prevBusy) {
            this._incrementProcessing();
        } else if (!currBusy && prevBusy) {
            this._decrementProcessing();
        }
    }

    updateChangesState(state, prev) {
        if (!this.panelElement) return;

        if (prev && state.visible === prev.visible && state.changedFiles === prev.changedFiles) {
            return;
        }

        this._cachedFiles = state.changedFiles || [];
        const hasChanges = this._cachedFiles.length > 0;

        if (hasChanges) {
            this.panelElement.classList.remove('hidden');
        } else {
            this.panelElement.classList.add('hidden');
            this._isExpanded = false;
            this.panelElement.classList.remove('expanded');
            const body = this.panelElement.querySelector('.changes-body');
            if (body) {
                body.style.removeProperty('height');
                body.style.removeProperty('max-height');
            }
        }

        this._renderFiles();
        this._updateUiState();
    }

    async _handleReviewAll() {
        if (this.reviewAllBtn?.disabled) return;
        this._incrementProcessing();
        try {
            const nonDeletedPaths = this._cachedFiles
                .filter(f => (f.relativePath || f))
                .map(f => f.relativePath || f);
            if (nonDeletedPaths.length > 0) {
                await this.onReviewAll.emit(nonDeletedPaths);
            }
        } catch (error) {
            console.error('Critical error in _handleReviewAll:', error);
        } finally {
            this._decrementProcessing();
        }
    }

    async _handleOpenAll() {
        if (this.openAllBtn?.disabled) return;
        this._incrementProcessing();
        try {
            const nonDeletedPaths = this._cachedFiles
                .filter(f => (f.relativePath || f) && f.status !== 'deleted')
                .map(f => f.relativePath || f);
            if (nonDeletedPaths.length > 0) {
                await this.onOpenAll.emit(nonDeletedPaths);
            }
        } catch (error) {
            console.error('Critical error in _handleOpenAll:', error);
        } finally {
            this._decrementProcessing();
        }
    }

    async _handleDiscardAll() {
        if (this.discardAllBtn?.disabled) return;
        this._incrementProcessing();
        this._cachedFiles = [];
        try {
            await this.onDiscardAll.emit();
        } finally {
            this._decrementProcessing();
        }
    }

    async _handleAcceptAll() {
        if (this.acceptAllBtn?.disabled) return;
        this._incrementProcessing();
        this._cachedFiles = [];
        try {
            await this.onAcceptAll.emit();
        } finally {
            this._decrementProcessing();
        }
    }

    async _handleFileListClick(event) {
        if (this._processingCounter > 0) return;

        const target = event.target;
        const fileItem = target.closest('.file-item');
        if (!fileItem) return;

        const filePath = fileItem.getAttribute('data-file-path');
        if (!filePath) return;

        if (target.closest('.file-icon')) {
            event.stopPropagation();
            this._incrementProcessing();
            try {
                await this.onOpenFile.emit(filePath);
            } finally {
                this._decrementProcessing();
            }
            return;
        }

        if (target.closest('.action-discard')) {
            event.stopPropagation();
            this._incrementProcessing();
            try {
                fileItem.style.opacity = '0.4';
                await this.onDiscardSingleFile.emit(filePath);
            } finally {
                this._decrementProcessing();
            }
            return;
        }

        if (target.closest('.action-accept')) {
            event.stopPropagation();
            this._incrementProcessing();
            try {
                fileItem.style.opacity = '0.4';
                await this.onAcceptSingleFile.emit(filePath);
            } finally {
                this._decrementProcessing();
            }
            return;
        }

        this._incrementProcessing();
        try {
            await this.onReviewFile.emit(filePath);
        } finally {
            this._decrementProcessing();
        }
    }

    _togglePanel(event) {
        if (event.target.closest('.header-actions')) return;
        this._isExpanded = !this._isExpanded;
        if (this.panelElement) {
            this.panelElement.classList.toggle('expanded', this._isExpanded);
        }
    }

    _toggleViewMode(event) {
        if (event) event.stopPropagation();
        this._viewMode = this._viewMode === 'list' ? 'tree' : 'list';
        if (this.toggleViewModeBtn) {
            this.toggleViewModeBtn.innerHTML = this._viewMode === 'list'
                ? `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 12H4M4 6h16M4 18h16"/></svg>`
                : `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><path d="M9 12h6M15 5l-6 7 6 7"/></svg>`;
        }
        this._renderFiles();
    }

    _renderFiles() {
        if (!this.filesList) return;
        this.filesList.innerHTML = '';

        if (this._viewMode === 'tree') {
            this.filesList.className = 'files-list view-tree';
            this._renderTree();
        } else {
            this.filesList.className = 'files-list view-list';
            this._renderList();
        }

        const countElement = document.getElementById('global-changes-count');
        if (countElement) {
            countElement.textContent = `(${this._cachedFiles.length} file${this._cachedFiles.length !== 1 ? 's' : ''})`;
        }
    }

    _renderList() {
        this._cachedFiles.forEach(fileChange => {
            const filePath = fileChange.relativePath || fileChange;
            const status = fileChange.status || 'modified';
            const normalizedPath = filePath.replace(/\\/g, '/');
            const lastSlashIndex = normalizedPath.lastIndexOf('/');

            let fileName = filePath;
            let dirPath = '';

            if (lastSlashIndex !== -1) {
                fileName = filePath.substring(lastSlashIndex + 1);
                dirPath = filePath.substring(0, lastSlashIndex);
            }

            const fileItem = document.createElement('button');
            fileItem.className = 'file-item';
            fileItem.type = 'button';
            fileItem.setAttribute('data-file-path', filePath);
            fileItem.setAttribute('data-file-status', status);
            fileItem.setAttribute('title', `Click to view diff for ${filePath}`);

            const statusLabel = {
                'created': 'New',
                'deleted': 'Deleted',
                'modified': 'Modified'
            }[status] || status;

            fileItem.innerHTML = `
            <div class="file-info-block">
                <span class="file-icon" title="Click to open file in editor">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>
                </span>
                <span class="file-name">${fileName}</span>
                ${dirPath ? `<span class="file-path-dir">${dirPath}</span>` : ''}
                <span class="file-status file-status-${status}">${statusLabel}</span>
            </div>
            <div class="file-row-actions">
                <span class="action-btn action-discard" title="Discard modifications">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
                </span>
                <span class="action-btn action-accept" title="Accept modifications">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>
                </span>
            </div>
        `;
            this.filesList.appendChild(fileItem);
        });
    }

    _renderTree() {
        const treeRoot = {};

        this._cachedFiles.forEach(fileChange => {
            const filePath = fileChange.relativePath || fileChange;
            const status = fileChange.status || 'modified';
            const parts = filePath.replace(/\\/g, '/').split('/');
            let current = treeRoot;
            parts.forEach((part, index) => {
                if (!current[part]) {
                    current[part] = {
                        name: part,
                        isElement: index === parts.length - 1,
                        fullPath: filePath,
                        status: status,
                        children: {}
                    };
                }
                current = current[part].children;
            });
        });

        const compactTree = (nodes) => {
            const compacted = {};

            Object.values(nodes).forEach(node => {
                if (node.children && Object.keys(node.children).length > 0) {
                    node.children = compactTree(node.children);
                }

                const childKeys = Object.keys(node.children);
                if (!node.isElement && childKeys.length === 1) {
                    const singleChild = node.children[childKeys[0]];
                    if (!singleChild.isElement) {
                        singleChild.name = `${node.name}/${singleChild.name}`;
                        compacted[singleChild.name] = singleChild;
                        return;
                    }
                }

                compacted[node.name] = node;
            });

            return compacted;
        };

        const compactedRoot = compactTree(treeRoot);

        const renderNode = (nodes, depth = 0) => {
            Object.values(nodes).forEach(node => {
                const row = document.createElement('button');
                row.type = 'button';
                row.style.paddingLeft = `${depth * 12 + 12}px`;

                if (node.isElement) {
                    row.className = 'file-item tree-file';
                    row.setAttribute('data-file-path', node.fullPath);
                    row.setAttribute('data-file-status', node.status);
                    row.setAttribute('title', `Click to view diff for this file`);

                    const statusLabel = {
                        'created': 'New',
                        'deleted': 'Deleted',
                        'modified': 'Modified'
                    }[node.status] || node.status;

                    row.innerHTML = `
                        <div class="file-info-block">
                            <span class="file-icon" title="Click to open file in editor">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>
                            </span>
                            <span class="file-name">${node.name}</span>
                            <span class="file-status file-status-${node.status}">${statusLabel}</span>
                        </div>
                        <div class="file-row-actions">
                            <span class="action-btn action-discard" title="Discard modifications">
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
                            </span>
                            <span class="action-btn action-accept" title="Accept modifications">
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>
                            </span>
                        </div>
                    `;
                } else {
                    row.className = 'tree-folder-row';
                    row.innerHTML = `
                        <div class="file-info-block">
                            <span class="file-icon">
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>
                            </span>
                            <span class="folder-name">${node.name}</span>
                        </div>
                    `;
                }
                this.filesList.appendChild(row);

                if (Object.keys(node.children).length > 0) {
                    renderNode(node.children, depth + 1);
                }
            });
        };

        renderNode(compactedRoot);
    }
}

const changesPanelComponent = new ChangesPanelComponent();
export { changesPanelComponent };