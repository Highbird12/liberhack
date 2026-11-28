(() => {
    const section = document.getElementById('around-the-world');
    if (!section) return;

    const canvas = section.querySelector('.literary-map-canvas');
    const picker = section.querySelector('select');
    const status = section.querySelector('[role="status"]');
    const results = section.querySelector('.literary-map-results');
    const resultTitle = results.querySelector('h3');
    const resultGrid = results.querySelector('.book-grid');
    const cards = [...document.querySelectorAll('#collection .book-card')];
    const options = new Map();
    const countryLayers = new Map();
    const countryCodes = new Map();
    let map;
    let mapBoundaries;
    let selectedLayer;
    let initialization;

    // The catalogue stores free-text nationality, while the map uses country boundaries.
    const aliases = new Map();
    const nationalityAliases = {
        GBR: ['British', 'English', 'Scottish', 'Welsh', 'Northern Irish', 'England', 'Scotland', 'Wales', 'Northern Ireland', 'UK', 'U.K.', 'Great Britain'],
        IRL: ['Irish', 'Republic of Ireland'],
        FRA: ['French'],
        RUS: ['Russian'],
        USA: ['American', 'US', 'U.S.', 'U.S.A.', 'United States of America'],
        DEU: ['German'],
        ESP: ['Spanish'],
        ITA: ['Italian'],
        PRT: ['Portuguese'],
        BGR: ['Bulgarian'],
        ROU: ['Romanian'],
        POL: ['Polish'],
        CZE: ['Czech'],
        SVK: ['Slovak'],
        GRC: ['Greek'],
        NLD: ['Dutch', 'Holland'],
        BEL: ['Belgian'],
        AUT: ['Austrian'],
        CHE: ['Swiss'],
        HUN: ['Hungarian'],
        UKR: ['Ukrainian'],
        SWE: ['Swedish'],
        NOR: ['Norwegian'],
        DNK: ['Danish'],
        FIN: ['Finnish'],
        ISL: ['Icelandic'],
        TUR: ['Turkish'],
        CAN: ['Canadian'],
        MEX: ['Mexican'],
        BRA: ['Brazilian'],
        ARG: ['Argentine', 'Argentinian'],
        CHL: ['Chilean'],
        COL: ['Colombian'],
        PER: ['Peruvian'],
        CUB: ['Cuban'],
        JPN: ['Japanese'],
        CHN: ['Chinese'],
        IND: ['Indian'],
        PAK: ['Pakistani'],
        AUS: ['Australian'],
        NZL: ['New Zealander'],
        EGY: ['Egyptian'],
        NGA: ['Nigerian'],
        ZAF: ['South African'],
        KEN: ['Kenyan'],
        MAR: ['Moroccan']
    };
    const ambiguousLabels = new Set(['congo', 'congolese', 'korea', 'korean', 'soviet', 'yugoslav']);

    function normalize(value) {
        return (value ?? '').normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase()
            .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
    }

    for (const [code, labels] of Object.entries(nationalityAliases)) {
        for (const label of labels) aliases.set(normalize(label), code);
    }

    function bookCount(count) {
        return count === 1 ? '1 book' : `${count} books`;
    }

    function indexCountries(features) {
        const labels = new Map();
        for (const { properties } of features) {
            const { adm0_a3: code, name_en, name, admin } = properties;
            const country = { name: name_en || name || admin, cards: [] };
            options.set(code, country);

            const { iso_a2, iso_a2_eh, iso_a3, iso_a3_eh, name_long, formal_en } = properties;
            for (const countryCode of [code, iso_a2, iso_a2_eh, iso_a3, iso_a3_eh]) {
                if (countryCode && countryCode !== '-99') countryCodes.set(countryCode, code);
            }
            for (const label of [name, name_en, name_long, admin, formal_en]) {
                if (!label) continue;
                const key = normalize(label);
                if (!labels.has(key)) labels.set(key, new Set());
                labels.get(key).add(code);
            }
        }
        return labels;
    }

    function groupBooks(features) {
        const countryLabels = indexCountries(features);
        const unmapped = new Map();
        for (const card of cards) {
            const nationality = (card.dataset.nationality ?? '').trim();
            const key = normalize(nationality);
            const matchingCodes = countryLabels.get(key);
            const alias = aliases.get(key);
            const countryCode = countryCodes.get(alias || nationality.toUpperCase()) ??
                (matchingCodes?.size === 1 ? matchingCodes.values().next().value : undefined);

            if (countryCode && !ambiguousLabels.has(key)) {
                options.get(countryCode).cards.push(card);
            } else {
                if (!unmapped.has(key)) {
                    unmapped.set(key, { name: nationality || 'Unspecified nationality', cards: [] });
                }
                unmapped.get(key).cards.push(card);
            }
        }
        return unmapped;
    }

    function appendPickerGroup(label, entries) {
        if (!entries.length) return;
        const group = document.createElement('optgroup');
        group.label = label;
        entries.sort(([, a], [, b]) => a.name.localeCompare(b.name));
        for (const [key, entry] of entries) {
            group.append(new Option(`${entry.name} (${bookCount(entry.cards.length)})`, key));
        }
        picker.append(group);
    }

    function populatePicker(unmapped, hasBoundaries) {
        const countries = [...options];
        appendPickerGroup('Countries with books', countries.filter(([, entry]) => entry.cards.length));
        appendPickerGroup('Other countries', countries.filter(([, entry]) => !entry.cards.length));
        const unmatchedEntries = [...unmapped.values()].map((entry, index) => [`unmapped-${index}`, entry]);
        for (const [key, entry] of unmatchedEntries) options.set(key, entry);
        appendPickerGroup(hasBoundaries ? 'Unmapped nationalities' : 'Catalogue nationalities', unmatchedEntries);
        picker.disabled = options.size === 0;
    }

    function selectCountry(key) {
        const entry = options.get(key);
        const layer = countryLayers.get(key);

        if (selectedLayer) selectedLayer.setStyle(countryStyle(selectedLayer.feature));
        selectedLayer = layer;
        if (layer) {
            layer.setStyle({ color: '#394c2f', weight: 2, fillColor: '#76855f', fillOpacity: 0.95 });
            layer.bringToFront();
        }

        picker.value = entry ? key : '';
        results.hidden = !entry;
        resultGrid.replaceChildren();
        if (!entry) {
            resultTitle.textContent = '';
            return;
        }

        resultTitle.textContent = `${entry.name} · ${bookCount(entry.cards.length)}`;
        if (!entry.cards.length) {
            const empty = document.createElement('p');
            empty.textContent = 'No books are available for this country yet.';
            resultGrid.append(empty);
            return;
        }

        for (const card of entry.cards) {
            const copy = card.cloneNode(true);
            copy.removeAttribute('id');
            resultGrid.append(copy);
        }
    }

    function countryStyle(feature) {
        const hasBooks = options.get(feature.properties.adm0_a3).cards.length > 0;
        return {
            color: '#a49f90',
            weight: 0.8,
            fillColor: hasBooks ? '#81936b' : '#e3dccb',
            fillOpacity: 0.9
        };
    }

    function createMap(boundaries) {
        const world = [[-64, -180], [84, 180]];
        const L = window.L;
        map = L.map(canvas, {
            crs: L.CRS.EPSG4326,
            maxBounds: [[-90, -180], [90, 180]],
            maxBoundsViscosity: 0.85,
            minZoom: 0,
            maxZoom: 6,
            zoomSnap: 0.25,
            scrollWheelZoom: false
        });
        map.attributionControl.addAttribution('Made with Natural Earth');
        map.fitBounds(world);
        L.geoJSON(boundaries, {
            style: countryStyle,
            onEachFeature(feature, layer) {
                const code = feature.properties.adm0_a3;
                const { name, cards: countryCards } = options.get(code);
                const label = document.createElement('span');
                label.textContent = `${name} · ${bookCount(countryCards.length)}`;
                layer.bindTooltip(label, { sticky: true });
                countryLayers.set(code, layer);
                layer.on({
                    mouseover() {
                        if (layer !== selectedLayer) layer.setStyle({ color: '#5b694c', weight: 1.6 });
                    },
                    mouseout() {
                        if (layer !== selectedLayer) layer.setStyle(countryStyle(feature));
                    },
                    click() {
                        selectCountry(code);
                    }
                });
            }
        }).addTo(map);
    }

    function loadAsset(element, url) {
        return new Promise((resolve, reject) => {
            element.onload = resolve;
            element.onerror = reject;
            if (element instanceof HTMLLinkElement) element.href = url;
            else element.src = url;
            document.head.append(element);
        });
    }

    async function loadBoundaries() {
        const response = await fetch(section.dataset.boundaries);
        if (!response.ok) throw new Error('Country boundaries could not be loaded.');
        const boundaries = await response.json();
        if (boundaries.type !== 'FeatureCollection' || !boundaries.features?.length) {
            throw new Error('Country boundaries are invalid.');
        }
        return boundaries;
    }

    async function initialize() {
        status.textContent = 'Loading the map…';
        const stylesheet = document.createElement('link');
        stylesheet.rel = 'stylesheet';
        const [boundaries, script, styles] = await Promise.allSettled([
            loadBoundaries(),
            loadAsset(document.createElement('script'), section.dataset.leafletScript),
            loadAsset(stylesheet, section.dataset.leafletStyle)
        ]);
        const geography = boundaries.status === 'fulfilled' ? boundaries.value : null;
        const unmapped = groupBooks(geography?.features ?? []);
        populatePicker(unmapped, Boolean(geography));

        if (geography && script.status === 'fulfilled' && styles.status === 'fulfilled') {
            mapBoundaries = geography;
        } else {
            showMapFallback();
        }
    }

    function showMapFallback() {
        canvas.hidden = true;
        status.textContent = picker.disabled
            ? 'The map is unavailable and the catalogue has no books to display.'
            : 'The map is unavailable. Use the list to browse the collection.';
    }

    function refreshMap() {
        if (section.hidden) return;
        if (map) {
            map.invalidateSize();
        } else if (mapBoundaries) {
            try {
                createMap(mapBoundaries);
                status.textContent = '';
            } catch {
                map?.remove();
                map = undefined;
                mapBoundaries = undefined;
                showMapFallback();
            }
        }
    }

    function updateVisibility() {
        const open = location.hash === '#around-the-world';
        section.hidden = !open;
        if (!open) return;
        initialization ??= initialize().then(refreshMap);
        requestAnimationFrame(() => {
            if (section.hidden) return;
            refreshMap();
            const heading = section.querySelector('h2');
            heading.focus({ preventScroll: true });
            section.scrollIntoView();
        });
    }

    picker.addEventListener('change', () => selectCountry(picker.value));
    section.querySelector('.literary-map-header a').addEventListener('click', () => {
        requestAnimationFrame(() => document.getElementById('collection').focus());
    });
    window.addEventListener('hashchange', updateVisibility);
    updateVisibility();
})();
