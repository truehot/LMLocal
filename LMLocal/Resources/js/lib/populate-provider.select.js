/**
 * Populates a <select> element with provider options and selects the matching one.
 *
 * Accepts either:
 *   { defaultProviders, providers } — from store/getProvidersAsync
 *   [...] — flat array (Autocompletions)
 *
 * Matching is by providerType + id. Falls back to first provider if no match.
 */
export function populateProviderSelect(select, providers, currentSettings) {
    const list = Array.isArray(providers)
        ? providers
        : [...(providers.defaultProviders || []), ...(providers.providers || [])];

    const savedType = currentSettings?.Provider ?? currentSettings?.providerType;
    const savedProviderId = currentSettings?.ProviderId ?? currentSettings?.providerId;
    const savedUrl = currentSettings?.LmStudioBaseUrl;
    const savedApiKey = currentSettings?.ApiKey;

    let matchIdx = 0;
    let matched = false;

    for (let i = 0; i < list.length; i++) {
        const p = list[i];
        if (!p) continue;

        if (
            savedProviderId != null &&
            p.providerType === savedType &&
            p.id === savedProviderId
        ) {
            matchIdx = i;
            matched = true;
            break;
        }

        if (
            !matched &&
            p.providerType === savedType &&
            p.customBaseUrl === savedUrl &&
            (!p.customApiKey || p.customApiKey === savedApiKey)
        ) {
            matchIdx = i;
            matched = true;
        }
    }

    select.innerHTML = '';
    list.forEach((provider, idx) => {
        const option = document.createElement('option');
        option.value = idx;
        option.textContent = provider.name;
        option._providerData = provider;
        if (idx === matchIdx) {
            option.selected = true;
        }
        select.appendChild(option);
    });

    return select.value;
}
