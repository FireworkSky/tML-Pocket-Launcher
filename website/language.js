// Explicit language URLs also work when scripts or preference storage are disabled.
(() => {
    const switcher = document.querySelector('.language-link');
    const key = 'tml-pocket-launcher.website.language';
    const destination = new URL(switcher.href);

    if (location.pathname.endsWith('/')) {
        let preference;
        try {
            preference = localStorage.getItem(key);
        } catch {
            // Some browsers disable storage in private windows.
        }
        const languages = navigator.languages?.length ? navigator.languages : [navigator.language];
        const supported = languages.find(language => /^(zh|en)(-|$)/i.test(language));
        if ((preference ?? supported)?.toLowerCase().startsWith('en')) {
            destination.search = location.search;
            destination.hash = location.hash;
            location.replace(destination);
            return;
        }
    }

    function updateLink() {
        destination.hash = location.hash;
        destination.search = location.search;
        switcher.href = destination.href;
    }
    updateLink();
    window.addEventListener('hashchange', updateLink);
    switcher.addEventListener('click', () => {
        try {
            localStorage.setItem(key, switcher.hreflang);
        } catch {
            // The link itself still selects the language.
        }
    });
})();
