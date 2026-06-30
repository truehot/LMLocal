/**
 * Populates a <select> element with provider options and selects the matching one.
 * Uses a single loop: finds the match index first, then builds options with selected
 * set during rendering.
 */
export function populateProviderSelect(select, allProviders, currentSettings) {
    const providers = [...(allProviders.defaultProviders || []), ...(allProviders.providers || [])];

    const savedType = currentSettings?.Provider;
    const savedUrl = currentSettings?.LmStudioBaseUrl;
    const savedKey = currentSettings?.ApiKey;


    let matchIdx = 0;
    for (let i = 0; i < providers.length; i++) {
        const p = providers[i];
        if (
            p &&
            p.providerType === savedType &&
            p.customBaseUrl === savedUrl &&
            p.customApiKey === savedKey
        ) {
            matchIdx = i;
            break;
        }
    }

    select.innerHTML = '';
    providers.forEach((provider, idx) => {
        const option = document.createElement('option');
        option.value = provider.id;
        option.textContent = provider.name;
        option._providerData = provider;
        if (idx === matchIdx) {
            option.selected = true;
        }
        select.appendChild(option);
    });

    return select.value;
}
