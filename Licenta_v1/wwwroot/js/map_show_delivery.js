document.addEventListener("DOMContentLoaded", function () {
    initApp();
});

function loadFailedOrderIds() {
    const el = document.getElementById("failedOrderIds");
    return el ? JSON.parse(el.value) : [];
}

// Functia principala de initializare
function initApp() {
    if (isDriver()) {
        // Pentru sofer, incarc comenzile si configurez traseul
        var ordersData = loadOrders();
        if (!ordersData) return;

        // remove anything already failed in the DB
        const origFailedFromInput = loadFailedOrderIds();
        const failedFromStatus = (ordersData || []).filter(o => o.Status === 3).map(o => o.id);
        const origFailed = [...new Set([...origFailedFromInput, ...failedFromStatus])];
        // split into available vs unavailable
        const allWithCoords = filterOrders(ordersData);

        // Keep all orders on the map for reference
        window.allOrders = allWithCoords;

        // But tag each one with its status for logic
        window.failedOrderIds = origFailed;

        const availableOrders = allWithCoords.filter(o => !origFailed.includes(o.id)); // ✅ ADD THIS LINE
        const unavailableOrders = allWithCoords.filter(o => origFailed.includes(o.id));

        if (!allWithCoords.length) {
            console.error("Nu exista comenzi cu coordonate valide.");
            return;
        }

        initMapForDriver(availableOrders, unavailableOrders);
        var deliveryId = getDeliveryId();
        if (!deliveryId) return;
        // Preiau currentSegment din localStorage (sau il initializez la 0)
        var storedSegment = localStorage.getItem("currentSegment_" + deliveryId);
        window.currentSegment = storedSegment !== null ? parseInt(storedSegment, 10) : 0;
        setupFollowMode();
        fetchRouteAndSetup(deliveryId);
        setupZoomControlListeners();
        setupMarkDeliveredButtons();
        setupMarkFailedButtons();
        setTimeout(checkDeliveryStatus, 500);
    } else {
        // Pentru non-sofer, afisez pe harta doar marker-ul pentru Headquarter si comenzile din zona
        initMapForNonDriver();
    }
}

// Verific daca utilizatorul este sofer (valorile din input-ul hidden "isDriver")
function isDriver() {
    var isDriverElement = document.getElementById("isDriver");
    return isDriverElement ? (isDriverElement.value === "true") : false;
}

// Incarc comenzile din input-ul hidden
function loadOrders() {
    var ordersDataElement = document.getElementById("ordersData");
    if (!ordersDataElement) {
        console.error("Nu s-a gasit elementul pentru orders data.");
        return null;
    }
    try {
        var ordersData = JSON.parse(ordersDataElement.value);
        if (!ordersData || ordersData.length === 0) {
            console.error("Nu s-au gasit comenzi pentru aceasta livrare.");
            return null;
        }
        console.log("Orders Data:", ordersData);
        return ordersData;
    } catch (e) {
        console.error("Eroare la orders data:", e);
        return null;
    }
}

// Filtrez comenzile care au coordonate valide
function filterOrders(orders) {
    return orders.filter(function (o) {
        return !isNaN(o.latitude) && !isNaN(o.longitude);
    });
}

// Initializez harta pentru sofer cu marker-ele pentru comenzi
function initMapForDriver(available, unavailable) {
    window.map = L.map('map', { zoomControl: true });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);

    // fit‐bounds container
    const bounds = L.latLngBounds();

    const okIcon = L.icon({
        iconUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-icon.png',
        shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41]
    });

    const nokIcon = L.icon({
        iconUrl: 'https://cdn-icons-png.flaticon.com/512/2576/2576762.png',
        iconSize: [32, 32],
        iconAnchor: [16, 32],
        popupAnchor: [0, -32]
    });

    available.forEach(o => {
        const m = L.marker([o.latitude, o.longitude], { icon: okIcon })
            .addTo(window.map)
            .bindPopup(`<b>Order ${o.id}</b><br>${o.address}`);
        bounds.extend(m.getLatLng());
    });

    unavailable.forEach(o => {
        const m = L.marker([o.latitude, o.longitude], { icon: nokIcon })
            .addTo(window.map)
            .bindPopup(`<b>Order ${o.id}</b><br><i>Unable to be delivered!</i>`);
        bounds.extend(m.getLatLng());
    });

    if (available.length + unavailable.length > 1) {
        window.map.fitBounds(bounds, { padding: [50, 50] });
    } else {
        const single = (available[0] || unavailable[0]);
        window.map.setView([single.latitude, single.longitude], 14);
    }
}

// Initializez harta pentru non-sofer, cu marker pentru Headquarter si comenzile din zona
function initMapForNonDriver() {
    const hqLat = +document.getElementById("headquarterLat").value;
    const hqLng = +document.getElementById("headquarterLng").value;
    const hqLL = [hqLat, hqLng];

    window.map = L.map('map', { zoomControl: true })
        .setView(hqLL, 13);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);

    // HQ icon
    const hqIcon = L.icon({
        iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
        iconSize: [32, 32], iconAnchor: [16, 32]
    });
    L.marker(hqLL, { icon: hqIcon })
        .addTo(window.map)
        .bindPopup("<b>Headquarter</b>");

    const ordersData = loadOrders() || [];
    const origFailed = loadFailedOrderIds();

    const okIcon = L.icon({
        iconUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-icon.png',
        shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41]
    });

    const nokIcon = L.icon({
        iconUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-icon.png',
        shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41]
    });

    const bounds = L.latLngBounds([hqLL]);
    ordersData.forEach(o => {
        if (!o.latitude || !o.longitude) return;
        const isBad = origFailed.includes(o.id);
        const marker = L.marker([o.latitude, o.longitude], {
            icon: isBad ? nokIcon : okIcon
        }).addTo(window.map)
            .bindPopup(
                `<b>Order ${o.id}</b><br>${o.address}` +
                (isBad ? "<br><i>Unable to be delivered!</i>" : "")
            );
        bounds.extend(marker.getLatLng());
    });

    if (bounds.isValid()) window.map.fitBounds(bounds, { padding: [50, 50] });
}

// Preiau deliveryId-ul din input-ul hidden
function getDeliveryId() {
    var deliveryIdElement = document.getElementById("deliveryId");
    if (!deliveryIdElement) {
        console.error("Nu s-a gasit elementul pentru delivery id.");
        return null;
    }
    return deliveryIdElement.value;
}

// Configurez modul de follow si evenimentele aferente
function setupFollowMode() {
    window.followMode = true;
    window.userMarker = null;
    window.ignoreNextMovestart = false;
    window.refocusButton = document.getElementById("refocusButton");

    function updateRefocusButtonStyle(follow) {
        if (window.refocusButton) {
            if (follow) {
                window.refocusButton.style.backgroundColor = 'white';
                window.refocusButton.style.color = 'blue';
            } else {
                window.refocusButton.style.backgroundColor = 'blue';
                window.refocusButton.style.color = 'white';
            }
        }
    }
    window.updateRefocusButtonStyle = updateRefocusButtonStyle;
    updateRefocusButtonStyle(window.followMode);

    window.map.on('movestart', function () {
        if (window.ignoreNextMovestart) {
            window.ignoreNextMovestart = false;
        } else {
            window.followMode = false;
            updateRefocusButtonStyle(window.followMode);
        }
    });

    if (window.refocusButton) {
        window.refocusButton.addEventListener("click", function () {
            window.followMode = true;
            updateRefocusButtonStyle(window.followMode);
            if (window.userMarker) {
                window.ignoreNextMovestart = true;
                window.map.panTo(window.userMarker.getLatLng(), { animate: true });
            }
        });
    }
    watchUserPosition();
}

// Urmaresc pozitia userului (doar pentru sofer)
function watchUserPosition() {
    if (!isDriver()) return;
    var carIcon = L.icon({
        iconUrl: '/Images/car.png',
        iconSize: [32, 32],
        iconAnchor: [16, 16]
    });
    navigator.geolocation.watchPosition(function (position) {
        var lat = position.coords.latitude;
        var lng = position.coords.longitude;
        var latlng = [lat, lng];

        if (!window.userMarker) {
            window.userMarker = L.marker(latlng, { icon: carIcon, rotationAngle: 0 })
                .addTo(window.map)
                .bindPopup("Your location");
        } else {
            window.userMarker.setLatLng(latlng);
        }

        console.log("Follow mode =", window.followMode);
        window.updateRefocusButtonStyle(window.followMode);
        if (window.followMode) {
            console.log("Harta se centreaza pe:", latlng);
            window.ignoreNextMovestart = true;
            window.map.panTo(latlng, { animate: true });
        }
    }, function (error) {
        if (error.code === error.PERMISSION_DENIED) {
            alert("Acceptati monitorizarea GPS pentru a vedea pozitia pe harta.");
        } else {
            console.error("Eroare la obtinerea locatiei GPS:", error);
        }
    }, { enableHighAccuracy: true });
}

// Preiau ruta optima si configurez marker-ele si afisez segmentului curent
function fetchRouteAndSetup(deliveryId) {
    const cacheKey = `routeData_${deliveryId}`;
    const cachedRaw = localStorage.getItem(cacheKey);

    if (cachedRaw) {
        const cached = JSON.parse(cachedRaw);
        const now = Date.now();

        if (now - cached.timestamp < 3600000) { // o ora
            console.log("Using cached route data (still valid)...");
            setupRouteDisplay(cached.data);
            return;
        } else {
            console.log("Cached route expired. Fetching new data...");
            localStorage.removeItem(cacheKey);
        }
    }

    fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId)
        .then(response => response.json())
        .then(data => {
            console.log("API Response:", data);
            if (data.error) {
                alert("Error: " + data.error);
                return;
            }

            const payload = {
                timestamp: Date.now(),
                data: data
            };

            localStorage.setItem(cacheKey, JSON.stringify(payload));
            setupRouteDisplay(data);
            console.log("DATA primit în JS:", data);
            console.log("AvoidPolygons în data:", data.avoidPolygons);
        })
        .catch(error => {
            console.error("Eroare la preluarea rutei:", error);
        });
}

const MOCK_MODE = localStorage.getItem("mock_mode") === "true";

document.addEventListener("keydown", function (e) {
    const key = e.key.toLowerCase();

    // Ctrl + M pentru start la mock mode
    if (e.ctrlKey && !e.shiftKey && key === "m") {
        const secret = prompt("Secret question: Who's the GOAT of football?('Name Surname')");
        if (secret && secret.trim().toLowerCase() === "demba ba") {
            localStorage.setItem("mock_mode", "true");
            alert("Mock Mode enabled. Reloading...");
            location.reload();
        } else {
            alert("Wrong answer.");
        }
    }

    // Ctrl + Shift + M pentru stop la mock mode
    if (e.ctrlKey && e.shiftKey && key === "m") {
        localStorage.removeItem("mock_mode");
        alert("Mock Mode disabled. Reloading...");
        location.reload();
    }
});

if (MOCK_MODE) {
    const badge = document.createElement("div");
    badge.textContent = "MOCK MODE";
    badge.style.cssText = `
        position: fixed;
        top: 10px;
        right: 10px;
        background: #ffc107;
        color: #000;
        padding: 5px 10px;
        font-weight: bold;
        z-index: 9999;
        border-radius: 5px;
        box-shadow: 0 0 5px rgba(0,0,0,0.2);
    `;
    document.body.appendChild(badge);
}

function setupRouteDisplay(data) {
    // 1) Ensure our custom panes exist
    if (!map.getPane('optimizedRoutePane')) {
        map.createPane('optimizedRoutePane');
        map.getPane('optimizedRoutePane').style.zIndex = 450;
    }
    if (!map.getPane('originalRoutePane')) {
        map.createPane('originalRoutePane');
        map.getPane('originalRoutePane').style.zIndex = 440;
    }

    // 2) Build the two polylines (but don’t add them yet)
    const rawCoords = (data.rawCoordinates || data.coordinates)
        .map(c => [c.latitude, c.longitude]);
    const finalCoords = data.coordinates
        .map(c => [c.latitude, c.longitude]);

    // 3) Clean up any old layers / controls
    ['initialRouteLayer', 'finalRouteLayer', 'routeControl', 'routeLegend'].forEach(key => {
        if (window[key]) {
            if (key.endsWith('Layer')) map.removeLayer(window[key]);
            else map.removeControl(window[key]);
            window[key] = null;
        }
    });

    // 4) Create the dashed “original” and solid “optimized” lines
    window.initialRouteLayer = L.polyline(rawCoords, {
        dashArray: '8,6', color: '#999', weight: 4, opacity: 0.9, pane: 'originalRoutePane'
    });
    window.finalRouteLayer = L.polyline(finalCoords, {
        color: '#0077cc', weight: 5, opacity: 0.9, pane: 'optimizedRoutePane'
    });

    // 5) Add the layers control & legend
    window.routeControl = L.control.layers(
        null,
        { 'Original route': window.initialRouteLayer, 'Optimized route': window.finalRouteLayer },
        { collapsed: false, position: 'topright' }
    ).addTo(map);

    window.routeLegend = L.control({ position: 'bottomleft' });
    window.routeLegend.onAdd = () => {
        const div = L.DomUtil.create('div', 'info legend');
        div.innerHTML = `
      <div style="background:#fff;padding:6px;border:1px solid #999;font-size:12px">
        <strong>Routes</strong><br>
        <i style="display:inline-block;width:18px;height:4px;border-top:4px dashed #999;margin-right:6px"></i>Original<br>
        <i style="display:inline-block;width:18px;height:4px;background:#0077cc;margin-right:6px"></i>Optimized
      </div>`;
        return div;
    };
    window.routeLegend.addTo(map);

    console.log("🔹 setupRouteDisplay 🔹");
    console.log("  • rawCoordinates length:", (data.rawCoordinates || data.coordinates).length);
    console.log("  • optimized coordinates length:", data.coordinates.length);
    console.log("  • stopIndices:", data.stopIndices);
    console.log("  • orderIds:", data.orderIds);
    console.log("  • incoming failedOrderIds:", data.failedOrderIds);

    // Step 6 FIXED: Correctly rebuild segments skipping failed orders
    window.failedOrderIds = new Set(data.failedOrderIds || window.failedOrderIds);

    window.routeCoords = finalCoords;
    window.stopIndices = data.stopIndices;
    window.orderIds = data.orderIds;
    window.segmentsData = data.segments;
    window.coloredRouteSegments = data.coloredRouteSegments;

    const goodStops = [];
    for (let i = 0; i < window.orderIds.length; i++) {
        if (!window.failedOrderIds.has(window.orderIds[i])) {
            goodStops.push({
                id: window.orderIds[i],
                coordIndex: window.stopIndices[i + 1],
                coords: window.routeCoords[window.stopIndices[i + 1]],
                segment: window.routeCoords.slice(window.stopIndices[i], window.stopIndices[i + 1] + 1),
                segmentData: window.segmentsData[i]
            });
        } else {
            console.warn("❌ Skipping failed orderId:", window.orderIds[i]);
        }
    }

    // rebuild clean coords/segments
    let rebuiltCoords = [window.routeCoords[0]];
    let rebuiltStopIndices = [0];
    let rebuiltSegmentsData = [];
    let rebuiltOrderIds = [];

    goodStops.forEach(stop => {
        rebuiltCoords.push(...stop.segment.slice(1)); // avoid duplicate first coord
        rebuiltStopIndices.push(rebuiltCoords.length - 1);
        rebuiltSegmentsData.push(stop.segmentData);
        rebuiltOrderIds.push(stop.id);
    });

    // HQ at end
    rebuiltCoords.push(window.routeCoords[window.routeCoords.length - 1]);
    rebuiltStopIndices.push(rebuiltCoords.length - 1);

    // update globals
    window.routeCoords = rebuiltCoords;
    window.stopIndices = rebuiltStopIndices;
    window.orderIds = rebuiltOrderIds;
    window.segmentsData = rebuiltSegmentsData;


    // ←––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––
    // 7) NEW: skip any already‐failed stops *before* drawing the segment
    // merge in any server‐sent failures
    window.failedOrderIds = data.failedOrderIds || window.failedOrderIds;

    // never exceed the last “order→HQ” leg
    console.log("  • previous currentSegment:", window.currentSegment);
    const maxSeg = window.stopIndices.length - 2;
    if (window.currentSegment > maxSeg) window.currentSegment = maxSeg;

    // skip forward past all failed orders
    while (
        window.currentSegment < window.orderIds.length &&
        window.failedOrderIds.includes(window.orderIds[window.currentSegment])
    ) {
        console.warn("  • Skipping failed orderId:", window.orderIds[window.currentSegment]);
        window.currentSegment++;
    }
    // clamp again
    if (window.currentSegment > maxSeg) window.currentSegment = maxSeg;
    console.log("  • final currentSegment:", window.currentSegment);
    // ←––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––––

    // 8) Draw the HQ marker
    const [hqLat, hqLng] = window.routeCoords[0];
    L.marker([hqLat, hqLng], {
        icon: L.icon({
            iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
            iconSize: [32, 32], iconAnchor: [16, 32], popupAnchor: [0, -32]
        })
    }).addTo(map).bindPopup('<b>Headquarter</b>');

    // 9) Finally wire up your helpers
    setupFullscreenControl();
    displayAllSegments();                      // now uses updated currentSegment
    displayColoredRouteSegments(window.coloredRouteSegments);
    displayAvoidPolygons(data.avoidPolygons, data.avoidDescriptions);
}


function displayWeatherPolygons(coloredSegments, step = 0.03) {
    if (!coloredSegments || coloredSegments.length === 0) return;

    if (window.weatherPolygonsLayerGroup) {
        window.map.removeLayer(window.weatherPolygonsLayerGroup);
    }
    window.weatherPolygonsLayerGroup = L.featureGroup().addTo(window.map);

    const halfStep = step / 2;
    const gridUsed = new Set();

    coloredSegments.forEach(segment => {
        const [a, b] = segment.coordinates;

        const midLat = (a[0] + b[0]) / 2;
        const midLng = (a[1] + b[1]) / 2;

        const key = getGridKey(midLat, midLng, step);
        if (gridUsed.has(key)) return;
        gridUsed.add(key);

        const polygon = L.rectangle([
            [midLat - halfStep, midLng - halfStep],
            [midLat + halfStep, midLng + halfStep]
        ], {
            color: getSeverityColorRGBA(segment.severity),
            weight: 5,
            fillOpacity: 0,
            dashArray: '3, 3'
        });

        polygon.bindPopup(`Poligon vreme (sev. max): ${segment.severity}`);
        polygon.addTo(window.weatherPolygonsLayerGroup);
    });
}

function getGridKey(lat, lng, step = 0.0125) {
    const bucketLat = Math.round(lat / step) * step;
    const bucketLng = Math.round(lng / step) * step;
    return `${bucketLat.toFixed(5)},${bucketLng.toFixed(5)}`;
}

function displayColoredRouteSegments(coloredSegments) {
    if (!coloredSegments || coloredSegments.length === 0) return;

    if (window.coloredSegmentsLayerGroup) {
        window.map.removeLayer(window.coloredSegmentsLayerGroup);
    }
    window.coloredSegmentsLayerGroup = L.featureGroup().addTo(window.map);

    const drawnSegments = new Set();

    function getSortedKey(a, b) {
        const [p1, p2] = [a, b].sort((x, y) => {
            if (x[0] !== y[0]) return x[0] - y[0];
            return x[1] - y[1];
        });
        return `${p1[0]},${p1[1]}|${p2[0]},${p2[1]}`;
    }

    // Limitam la subsegmentele care apar intre stopIndices[current] si stopIndices[next]
    const start = window.stopIndices[window.currentSegment];
    const end = window.stopIndices[window.currentSegment + 1];
    const segmentCoords = window.routeCoords.slice(start, end + 1);

    for (let i = 0; i < segmentCoords.length - 1; i++) {
        const segStart = segmentCoords[i];
        const segEnd = segmentCoords[i + 1];
        const segKey = getSortedKey(segStart, segEnd);

        const matching = coloredSegments.find(seg => {
            const [a, b] = seg.coordinates;
            return getSortedKey(a, b) === segKey;
        });

        if (!matching || drawnSegments.has(segKey)) continue;
        drawnSegments.add(segKey);

        const color = getSeverityColorRGBA(matching.severity);
        const emoji = getWeatherEmoji(matching.weatherCode);
        //const weatherDesc = (matching.severity === 0 ? "Clear" : (matching.weatherDescription || "unknown"))
        //    .replace(/\b\w/g, c => c.toUpperCase());
        const weatherDesc = getFriendlyWeatherDescription(emoji, matching.weatherDescription);

        const polyline = L.polyline([segStart, segEnd], {
            color: color,
            weight: 5,
            opacity: 0
        });

        polyline.bindPopup(`
            <b>⚠️ Severity:</b> ${getFormattedSeverity(matching.severity)}<br>
            <b>${emoji} Weather:</b> ${weatherDesc}
        `);

        polyline.addTo(window.coloredSegmentsLayerGroup);
    }
}

function getFormattedSeverity(severity) {
    return severity < 0.001 ? '0' : severity.toFixed(1);
}

function getWeatherEmoji(code) {
    // Thunderstorm
    if ([200, 201, 230, 231].includes(code)) return '⛈️🌦️'; // thunderstorm with (light) rain or drizzle
    if ([202, 232].includes(code)) return '⛈️🌧️'; // heavy thunderstorm with rain/drizzle
    if ([210, 221].includes(code)) return '🌩️'; // light/ragged thunderstorm
    if ([211].includes(code)) return '⛈️'; // normal thunderstorm
    if ([212].includes(code)) return '🌩️⚡'; // heavy thunderstorm

    // Drizzle
    if ([300, 310].includes(code)) return '🌦️'; // light drizzle
    if ([301, 311, 321].includes(code)) return '🌧️'; // drizzle
    if ([302, 312, 314].includes(code)) return '🌧️🌧️'; // heavy drizzle
    if ([313].includes(code)) return '🌧️🌦️'; // shower rain + drizzle

    // Rain
    if ([500].includes(code)) return '🌦️'; // light rain
    if ([501].includes(code)) return '🌧️'; // moderate rain
    if ([502, 503, 504].includes(code)) return '🌧️🌧️'; // heavy/very/extreme rain
    if ([511].includes(code)) return '🌧️❄️'; // freezing rain
    if ([520].includes(code)) return '🌦️'; // light shower rain
    if ([521].includes(code)) return '🌧️'; // shower rain
    if ([522, 531].includes(code)) return '🌧️🌧️'; // heavy/ragged shower rain

    // Snow
    if ([600].includes(code)) return '🌨️'; // light snow
    if ([601].includes(code)) return '❄️'; // snow
    if ([602, 622].includes(code)) return '❄️🌨️'; // heavy snow
    if ([611, 612, 613].includes(code)) return '🌧️❄️'; // sleet
    if ([615, 616].includes(code)) return '🌧️❄️'; // rain & snow
    if ([620, 621].includes(code)) return '🌨️'; // shower snow

    // Atmosphere
    if ([701].includes(code)) return '🌫️'; // mist
    if ([711].includes(code)) return '🚬'; // smoke
    if ([721].includes(code)) return '🌁'; // haze
    if ([731, 751, 761].includes(code)) return '🌪️'; // dust/sand
    if ([741].includes(code)) return '🌫️'; // fog
    if ([762].includes(code)) return '🌋'; // volcanic ash
    if ([771].includes(code)) return '💨'; // squall
    if ([781].includes(code)) return '🌪️'; // tornado

    // Clear & Clouds
    if (code === 800) return '☀️'; // clear sky
    if (code === 801) return '🌤️'; // few clouds
    if (code === 802) return '⛅'; // scattered clouds
    if (code === 803) return '🌥️'; // broken clouds
    if (code === 804) return '☁️'; // overcast clouds

    return '❔'; // unknown
}

function getFriendlyWeatherDescription(emoji, rawDesc) {
    const mapping = {
        '⛈️🌦️': 'Thunderstorm with light rain',
        '⛈️🌧️': 'Heavy thunderstorm with rain',
        '🌩️': 'Isolated thunderstorm',
        '⛈️': 'Thunderstorm',
        '🌩️⚡': 'Severe thunderstorm',
        '🌧️': 'Rainy weather',
        '🌦️': 'Light rain or drizzle',
        '🌧️🌧️': 'Heavy rain',
        '🌨️': 'Snowfall',
        '❄️': 'Moderate snow',
        '❄️🌨️': 'Heavy snowstorm',
        '🌧️❄️': 'Mixed rain and snow',
        '🌫️': 'Foggy or misty',
        '🌪️': 'Strong winds or tornado',
        '☀️': 'Clear skies',
        '🌤️': 'Mostly sunny',
        '⛅': 'Partly cloudy',
        '🌥️': 'Mostly cloudy',
        '☁️': 'Overcast',
        '🚬': 'Smoky air',
        '🌁': 'Hazy',
        '🌋': 'Volcanic ash',
        '💨': 'Strong gusts'
    };

    const fallback = rawDesc || "Unknown";
    return mapping[emoji] ? mapping[emoji] : fallback.replace(/\b\w/g, c => c.toUpperCase());
}

function getSeverityColorRGBA(severity) {
    if (severity >= 0.9) return 'rgba(188, 65, 250)'; // mov
    if (severity >= 0.7) return 'rgba(255, 65, 65)'; // rosu
    if (severity >= 0.5) return 'rgba(255, 165, 76)'; // portocaliu
    if (severity >= 0.3) return 'rgba(255, 215, 0)'; // galben
    return 'rgba(74, 255, 92)'; // verde
}

function getColorNameFromRGBA(rgba) {
    switch (rgba) {
        case 'rgba(188, 65, 250)': return 'mov';
        case 'rgba(255, 65, 65)': return 'roșu';
        case 'rgba(255, 165, 76)': return 'portocaliu';
        case 'rgba(255, 215, 0)': return 'galben';
        case 'rgba(74, 255, 92)': return 'verde';
        default: return 'necunoscut';
    }
}

function getContrastingColor(severity) {
    if (severity >= 0.9) return '#ffffff'; // contrast pe mov inchis
    if (severity >= 0.7) return '#ffffff'; // contrast pe rosu
    if (severity >= 0.5) return '#000000'; // contrast pe portocaliu
    if (severity >= 0.3) return '#000000'; // contrast pe galben
    return '#003300'; // contrast pe verde
}

// Configurez controlul fullscreen pentru harta
function setupFullscreenControl() {
    L.Control.Fullscreen = L.Control.extend({
        options: {
            position: 'topright',
            title: 'Fullscreen'
        },
        onAdd: function (map) {
            var container = L.DomUtil.create('div', 'leaflet-bar leaflet-control leaflet-control-custom');
            container.style.backgroundColor = 'white';
            container.style.width = '30px';
            container.style.height = '30px';
            container.style.cursor = 'pointer';
            container.style.backgroundImage = "url('/Images/fullscreen.svg')";
            container.style.backgroundSize = "20px 20px";
            container.style.backgroundPosition = "center";
            container.style.backgroundRepeat = "no-repeat";
            container.title = this.options.title;
            container.onclick = function () {
                toggleFullScreen();
            };
            return container;
        }
    });
    L.control.fullscreen = function (options) {
        return new L.Control.Fullscreen(options);
    };
    L.control.fullscreen().addTo(window.map);
}

// Comut modul fullscreen pentru harta
function toggleFullScreen() {
    window.ignoreNextMovestart = true;
    var mapContainer = document.getElementById('map-container');
    if (!document.fullscreenElement) {
        mapContainer.requestFullscreen().catch(function (err) {
            alert("Error activating full screen mode: " + err.message);
        });
    } else {
        document.exitFullscreen();
    }
}

// Configurez listener pentru butoanele de zoom
function setupZoomControlListeners() {
    var zoomInButton = document.querySelector('.leaflet-control-zoom-in');
    if (zoomInButton) {
        zoomInButton.addEventListener('click', function () {
            window.ignoreNextMovestart = true;
        });
    }
    var zoomOutButton = document.querySelector('.leaflet-control-zoom-out');
    if (zoomOutButton) {
        zoomOutButton.addEventListener('click', function () {
            window.ignoreNextMovestart = true;
        });
    }
}

// Configurez listener pentru butoanele "Mark as Delivered"
function setupMarkDeliveredButtons() {
    document.querySelectorAll(".mark-delivered-btn").forEach(function (button) {
        button.addEventListener("click", function (e) {
            e.preventDefault();
            var form = this.closest("form");
            var formData = new FormData(form);
            fetch(form.action, {
                method: form.method,
                body: formData
            })
            .then(function (response) {
                return response.text();
            })
            .then(function (text) {
                try {
                    var data = JSON.parse(text);
                    if (data.success) {
                        console.log("Order marcat ca nelivrat.");

                        // Fetch new route after successful marking as failed
                        fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId)
                            .then(res => res.json())
                            .then(newRouteData => {
                                // Update cache
                                const cacheKey = `routeData_${deliveryId}`;
                                localStorage.setItem(cacheKey, JSON.stringify({
                                    timestamp: Date.now(),
                                    data: newRouteData
                                }));

                                // Rerender the route immediately
                                setupRouteDisplay(newRouteData);
                            })
                            .catch(err => {
                                console.error("Error fetching updated route", err);
                            });

                    } else {
                        console.error("Eroare la marcarea comenzii ca nelivrate.");
                    }
                } catch (e) {
                    console.warn("Raspunsul nu este JSON valid, continuam...");
                }
            })
            .catch(function (error) {
                console.error("Eroare la marcarea comenzii ca livrate:", error);
            });
        });
    });
}

// Configurez listener pentru butoanele "Cannot Deliver"
function setupMarkFailedButtons() {
    document.querySelectorAll(".mark-failed-btn").forEach(function (button) {
        button.addEventListener("click", function (e) {
            e.preventDefault();
            var form = this.closest("form");
            var formData = new FormData(form);
            fetch(form.action, {
                method: form.method,
                body: formData
            })
            .then(function (response) {
                return response.text();
            })
            .then(function (text) {
                try {
                    var data = JSON.parse(text);
                    if (data.success) {
                        console.log("Order marcat ca nelivrat.");
                    } else {
                        console.error("Eroare la marcarea comenzii ca nelivrate.");
                    }
                } catch (e) {
                    console.warn("Raspunsul nu este JSON valid, continuam...");
                }
                advanceRoute();
                window.location.reload();
            })
            .catch(function (error) {
                console.error("Eroare la marcarea comenzii ca nelivrate:", error);
                advanceRoute();
                window.location.reload();
            });
        });
    });
}

function almostEqual(a, b, epsilon = 0.0001) {
    return Math.abs(a - b) < epsilon;
}

// Afisez toate segmentele din traseu, evidentiind segmentul curent
function displayAllSegments() {
    console.log("🔸 displayAllSegments 🔸");

    if (window.segmentsLayerGroup) {
        window.map.removeLayer(window.segmentsLayerGroup);
    }
    window.segmentsLayerGroup = L.featureGroup().addTo(window.map);

    let startIdx = window.stopIndices[window.currentSegment];
    let endIdx;

    console.log("  • startIdx:", startIdx, "→ stopIndices:", window.stopIndices);

    let nextSegment = window.currentSegment + 1;

    // Skip only failed orders directly ahead, without overshooting the HQ.
    while (
        nextSegment < window.orderIds.length &&
        window.failedOrderIds.includes(window.orderIds[nextSegment])
    ) {
        console.warn("  • Skipping nextSegment:", nextSegment, "due to failed orderId:", window.orderIds[nextSegment]);
        nextSegment++;
    }

    // Use the next valid stop if found, otherwise default to the final segment (HQ).
    if (nextSegment < window.stopIndices.length) {
        endIdx = window.stopIndices[nextSegment];
    } else {
        endIdx = window.stopIndices[window.stopIndices.length - 1]; // final HQ segment
    }

    console.log("  • Calculated endIdx:", endIdx);

    // Create a clean slice for the current segment.
    const coords = window.routeCoords.slice(startIdx, endIdx + 1);
    console.log("  • segment coords length:", coords.length);

    if (!coords.length) {
        console.error("  ✖ coords is empty! routeCoords:", window.routeCoords);
        return;
    }

    // Draw the polyline segment clearly
    const greenLine = L.polyline(coords, {
        color: 'rgba(74, 255, 92)',
        weight: 6,
        opacity: 1
    }).addTo(window.segmentsLayerGroup);

    // Polyline decorator (directional arrows)
    const currentSeverity = window.segmentsData[window.currentSegment]?.severity || 0;
    L.polylineDecorator(greenLine, {
        patterns: [{
            offset: '0%',
            repeat: '50px',
            symbol: L.Symbol.arrowHead({
                pixelSize: 10,
                polygon: false,
                pathOptions: {
                    stroke: true,
                    color: getContrastingColor(currentSeverity),
                    weight: 4
                }
            })
        }]
    }).addTo(window.segmentsLayerGroup);

    // Focus the map on the correctly calculated segment.
    window.map.fitBounds(
        window.segmentsLayerGroup.getBounds(),
        { padding: [50, 50] }
    );
}

// Avansez la urmatorul segment din ruta livrarii
function advanceRoute() {
    // start looking at the very next leg
    let next = window.currentSegment + 1;

    // skip *all* failed stops
    while (
        next < window.orderIds.length &&
        window.failedOrderIds.includes(window.orderIds[next])
    ) {
        next++;
    }

    // make sure we never go past the final “order→HQ” leg
    const maxSeg = window.stopIndices.length - 2;
    window.currentSegment = Math.min(next, maxSeg);

    localStorage.setItem('currentSegment_' + getDeliveryId(), window.currentSegment);
    displayAllSegments();
}


// Sterg toate segmentele si decoratorii de pe harta
function clearRouteSegments() {
    return new Promise(function (resolve) {
        if (window.currentSegmentLayer) {
            window.map.removeLayer(window.currentSegmentLayer);
        }
        if (window.currentDecorator) {
            window.map.removeLayer(window.currentDecorator);
        }
        window.map.eachLayer(function (layer) {
            if (layer instanceof L.Polyline && !(layer instanceof L.Marker)) {
                window.map.removeLayer(layer);
            } else if (layer instanceof L.LayerGroup) {
                window.map.removeLayer(layer);
            }
        });
        localStorage.setItem("routeCleared", "true");
        console.log("Toate segmentele si decoratorii au fost sterse. Raman doar comenzile si headquarter-ul.");
        resolve();
    });
}

// Verific statusul livrarii si sterg segmentele daca livrarea este completa    
function checkDeliveryStatus() {
    var deliveryStatusElement = document.getElementById("deliveryStatus");
    if (!deliveryStatusElement) {
        console.error("Nu s-a gasit elementul pentru statusul livrarii.");
        return;
    }
    var deliveryStatus = deliveryStatusElement.value.trim();
    console.log("Status curent livrare:", deliveryStatus);
    if (deliveryStatus === "Completed") {
        console.log("Livrarea este completa. Se asteapta initializarea hartii...");
        var mapCheckInterval = setInterval(function () {
            if (window.map) {
                clearRouteSegments();
                clearInterval(mapCheckInterval);
            }
        }, 200);
    }
}

function displayAvoidPolygons(avoidPolygonsData, descriptions = []) {
    console.log("Afișez poligoane:", avoidPolygonsData);
    if (!avoidPolygonsData || !Array.isArray(avoidPolygonsData)) return;

    const layerGroup = L.featureGroup().addTo(window.map);

    avoidPolygonsData.forEach((polygonGroup, index) => {
        const polygon = polygonGroup[0]; // GeoJSON
        const latLngs = polygon.map(coord => [coord[1], coord[0]]); // lat, lng

        const polygonLayer = L.polygon(latLngs, {
            color: 'black',
            fillColor: '#ff3333',
            fillOpacity: 0.6,
            weight: 2,
            dashArray: '4, 4'
        });

        const description = descriptions[index] || "🚧 Avoid zone";
        polygonLayer.bindPopup(`<b>🚧 Incident:</b> ${description}`);
        polygonLayer.addTo(layerGroup);
    });

    window.avoidPolygonsLayer = layerGroup;
}