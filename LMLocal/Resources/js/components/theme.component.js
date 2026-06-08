const ThemeMap = {
    0: 'dark',
    1: 'light',
    2: 'mid-light',
    3: 'mid-dark'
};

const themeMap = {
    0: 'hljs.dark.css',
    3: 'hljs.mid.dark.css',
    2: 'hljs.mid.light.css',
    1: 'hljs.light.css'
};

/**
 * ThemeComponent - manages the page theme and highlight.js stylesheet.
 */

class ThemeComponent {
    setup() {
        const currentSettings = { Theme: 0 };
        this._applyTheme(currentSettings.Theme);
        this._applyLink(currentSettings.Theme);
    }

    updateSettingsState(state, prevState) {
        if (state?.Theme !== prevState?.Theme) {
            this._applyTheme(state.Theme);
            this._applyLink(state.Theme);
        }
    }

    _applyTheme(themeValue) {
        const themeName = ThemeMap[themeValue] || 'dark';
        document.body.setAttribute('data-theme', themeName);
    }

    _applyLink(themeValue) {
        const link = document.getElementById('hljs-theme');
        if (!link) return;
        const baseUrl = 'https://app.local/css/';
        link.href = baseUrl + themeMap[themeValue];
    }
}

const themeComponent = new ThemeComponent();
export { themeComponent };
