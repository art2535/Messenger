(function () {
    function updateLayoutThemeIcons() {
        const isDark = document.documentElement.classList.contains('dark');
        document.querySelectorAll('.theme-icon-layout-dark').forEach(el => {
            el.style.display = isDark ? 'inline' : 'none';
        });
        document.querySelectorAll('.theme-icon-layout-light').forEach(el => {
            el.style.display = isDark ? 'none' : 'inline';
        });

        const metaTheme = document.querySelector('meta[name="theme-color"]:not([media])')
            || document.querySelector('meta[name="theme-color"]');
        if (metaTheme) {
            metaTheme.setAttribute('content', isDark ? '#0f172a' : '#ffffff');
        }
    }

    function toggleTheme() {
        const html = document.documentElement;
        const isDark = html.classList.toggle('dark');
        localStorage.setItem('theme', isDark ? 'dark' : 'light');
        updateLayoutThemeIcons();
    }

    const themeBtn = document.getElementById('theme-toggle-layout');
    if (themeBtn) {
        themeBtn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            toggleTheme();
        });
    }
    updateLayoutThemeIcons();

    const mobileNav = document.getElementById('mobileNav');
    const mobileBackdrop = document.getElementById('mobileNavBackdrop');
    const toggler = document.querySelector('.navbar-toggler');
    const mainEl = document.querySelector('main');
    const headerEl = document.querySelector('header');

    function updateMainMargin() {
        if (!mainEl || !headerEl) return;
        const bar = headerEl.querySelector('.navbar');
        const h = bar ? bar.offsetHeight : 56;
        mainEl.style.marginTop = h + 'px';
    }

    function closeMobileNav() {
        if (!mobileNav) return;
        mobileNav.classList.remove('show');
        if (mobileBackdrop) mobileBackdrop.classList.remove('show');
        if (toggler) toggler.setAttribute('aria-expanded', 'false');
    }

    function toggleMobileNav() {
        if (!mobileNav) return;
        const willOpen = !mobileNav.classList.contains('show');
        mobileNav.classList.toggle('show', willOpen);
        if (mobileBackdrop) mobileBackdrop.classList.toggle('show', willOpen);
        if (toggler) toggler.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
    }

    if (toggler) {
        toggler.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            toggleMobileNav();
        });
    }

    if (mobileBackdrop) {
        mobileBackdrop.addEventListener('click', closeMobileNav);
    }

    if (mobileNav) {
        mobileNav.querySelectorAll('a.mobile-nav-item').forEach(function (link) {
            link.addEventListener('click', closeMobileNav);
        });
    }

    document.addEventListener('click', function (e) {
        if (!mobileNav || !mobileNav.classList.contains('show')) return;
        if (mobileNav.contains(e.target)) return;
        if (toggler && toggler.contains(e.target)) return;
        closeMobileNav();
    });

    window.addEventListener('resize', function () {
        if (window.innerWidth >= 768) closeMobileNav();
        updateMainMargin();
    });

    updateMainMargin();
    window.addEventListener('load', updateMainMargin);
})();
