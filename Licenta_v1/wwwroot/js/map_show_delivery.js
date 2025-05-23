let lastPos = null;
let lastTs = null;

document.addEventListener("DOMContentLoaded", function () {
    initApp();
});

function loadFailedOrderIds() {
    const el = document.getElementById("failedOrderIds");
    return el ? JSON.parse(el.value) : [];
}

// Functia principala de initializare
function initApp() {
    const hqLat = parseFloat(document.getElementById("headquarterLat")?.value);
    const hqLng = parseFloat(document.getElementById("headquarterLng")?.value);
    if (isNaN(hqLat) || isNaN(hqLng)) {
        console.error("Coordonate HQ invalide.");
        return;
    }

    if (!isDriver()) {
        initMapForNonDriver();
        return;
    }

    window.map = L.map('map', { zoomControl: true }).setView([hqLat, hqLng], 13);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);
    const hqIcon = L.icon({
        iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
        iconSize: [32, 32],
        iconAnchor: [16, 32]
    });
    L.marker([hqLat, hqLng], { icon: hqIcon })
        .addTo(window.map)
        .bindPopup("<b>Headquarter</b>");

    // Daca n-am comenzi, nu incerc sa fac ruta
    const ordersData = loadOrders();
    if (!ordersData || ordersData.length === 0) {
        console.warn("Nu exista comenzi disponibile → afișez doar HQ.");
        return;   // map+HQ is already on screen
    }

    // Scot comenzile care nu au coordonate valide
    const allWithCoords = filterOrders(ordersData);
    if (allWithCoords.length === 0) {
        console.warn("Nu exista comenzi cu coordonate valide → afișez doar HQ.");
        return;
    }

    window.allOrders = allWithCoords;
    const deliveryId = getDeliveryId();
    window.currentSegment = parseInt(localStorage.getItem("currentSegment_" + deliveryId) || "0", 10);
    setupFollowMode();
    setupZoomControlListeners();
    setupMarkDeliveredButtons();
    setupMarkFailedButtons();
    fetchRouteAndSetup(deliveryId);
    setTimeout(checkDeliveryStatus, 500);
}

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
            //console.error("Nu s-au gasit comenzi pentru aceasta livrare.");
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

function drawOrderMarkers(orders) {
    if (!window.map) return;

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

    orders.forEach(order => {
        if (!order.latitude || !order.longitude) return;
        const marker = L.marker([order.latitude, order.longitude], {
            icon: (window.failedOrderIds.has(order.id) ? nokIcon : okIcon)
        }).addTo(window.map).bindPopup(`
            <b>Order ${order.id}</b><br>${order.address || ""}${window.failedOrderIds.has(order.id)
                ? "<br><i>Unable to be delivered!</i>" : ""
            }`);
        bounds.extend(marker.getLatLng());
    });

    if (bounds.isValid()) {
        window.map.fitBounds(bounds, { padding: [50, 50] });
    }
}

function initMapForNonDriver() {
    const hqLat = parseFloat(document.getElementById("headquarterLat")?.value);
    const hqLng = parseFloat(document.getElementById("headquarterLng")?.value);
    if (isNaN(hqLat) || isNaN(hqLng)) {
        console.error("Coordonate HQ invalide.");
        return;
    }

    const hqLL = [hqLat, hqLng];
    window.map = L.map('map', { zoomControl: true }).setView(hqLL, 13);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);

    const hqIcon = L.icon({
        iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
        iconSize: [32, 32],
        iconAnchor: [16, 32]
    });

    L.marker(hqLL, { icon: hqIcon })
        .addTo(window.map)
        .bindPopup("<b>Headquarter</b>");

    const orders = loadOrders() || [];

    window.failedOrderIds = new Set(loadFailedOrderIds());
    window.allOrders = orders;

    drawOrderMarkers(orders);
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
    const carIcon = L.icon({
        iconUrl: '/Images/car.png',
        iconSize: [32, 32],
        iconAnchor: [16, 16]
    });

    navigator.geolocation.watchPosition(pos => {
        const lat = pos.coords.latitude;
        const lng = pos.coords.longitude;
        const ts = new Date(pos.timestamp || Date.now());

        // Only POST when we have a previous point
        if (lastPos && lastTs) {
            // 1) compute speed & heading
            const dt = (ts - lastTs) / 1000; // seconds
            const R = 6371000;              // metres
            const φ1 = lastPos.lat * Math.PI / 180;
            const φ2 = lat * Math.PI / 180;
            const Δφ = (lat - lastPos.lat) * Math.PI / 180;
            const Δλ = (lng - lastPos.lng) * Math.PI / 180;
            const a = Math.sin(Δφ / 2) ** 2
                + Math.cos(φ1) * Math.cos(φ2) * Math.sin(Δλ / 2) ** 2;
            const d = 2 * R * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
            const speedKmh = d / dt * 3.6;
            const y = Math.sin(Δλ) * Math.cos(φ2);
            const x = Math.cos(φ1) * Math.sin(φ2)
                - Math.sin(φ1) * Math.cos(φ2) * Math.cos(Δλ);
            const headingDeg = (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;

            // 2) build DTO
            const payload = {
                driverId: document.getElementById("driverId").value,
                vehicleId: parseInt(document.getElementById("vehicleId").value, 10),
                timestamp: ts.toISOString(),
                latitude: lat,
                longitude: lng,
                speedKmh: speedKmh,
                headingDeg: headingDeg
            };

            // 3) POST
            fetch("/api/Telemetry", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            })
                .then(async r => {
                    if (!r.ok) {
                        const text = await r.text();
                        console.warn("Telemetry error", r.status, ":", text || r.statusText);
                    }
                })
                .catch(err => console.error("Telemetry failed:", err));
        }

        // 4) slide the window
        lastPos = { lat, lng };
        lastTs = ts;

        // 5) update the marker
        const latlng = [lat, lng];
        if (!window.userMarker) {
            window.userMarker = L.marker(latlng, { icon: carIcon })
                .addTo(window.map)
                .bindPopup("Your location");
        } else {
            window.userMarker.setLatLng(latlng);
        }
        if (window.followMode) {
            window.ignoreNextMovestart = true;
            window.map.panTo(latlng, { animate: true });
        }

    }, err => {
        if (err.code === err.PERMISSION_DENIED) {
            alert("Accepta monitorizarea GPS pentru a vedea poziția pe hartă.");
        } else {
            console.error("Geolocation error:", err);
        }
    }, { enableHighAccuracy: true });
}

const MOCK_MODE = localStorage.getItem("mock_mode") === "true";

async function fetchRouteAndSetup(deliveryId, forceRefresh = false) {

    if (MOCK_MODE) {
        console.log("MOCK MODE = fake route");

        const hqLat = parseFloat(document.getElementById("headquarterLat").value);
        const hqLng = parseFloat(document.getElementById("headquarterLng").value);

        const mockCoords = [
            [hqLat, hqLng],
            [hqLat + 0.005, hqLng + 0.002],
            [hqLat + 0.01, hqLng + 0.004],
            [hqLat + 0.015, hqLng + 0.006],
            [hqLat + 0.02, hqLng + 0.008],
            [hqLat + 0.025, hqLng + 0.01]
        ];

        const stopIndices = [0, 1, 2, 3, 4, 5];
        const orderIds = [101, 102, 103, 104];
        const severities = [0.2, 0.4, 0.6, 0.8, 0.95];
        const segments = stopIndices.slice(0, -1).map((from, i) => ({
            coordinates: [mockCoords[from], mockCoords[from + 1]],
            severity: severities[i % severities.length],
            weatherCode: 800 + i
        }));

        const mockData = {
            rawCoordinates: mockCoords.map(c => ({ latitude: c[0], longitude: c[1] })),
            coordinates: mockCoords.map(c => ({ latitude: c[0], longitude: c[1] })),
            stopIndices,
            orderIds,
            segments,
            coloredRouteSegments: segments,
            failedOrderIds: []
        };

        window.failedOrderIds = new Set();
        window.routeResult = mockData;

        mockData.coloredRouteSegments.forEach(seg => {
            const line = L.polyline(seg.coordinates, {
                color: getSeverityColorRGBA(seg.severity),
                weight: 6,
                opacity: 1
            }).addTo(window.map);

            L.polylineDecorator(line, {
                patterns: [{
                    offset: '3%',
                    repeat: '50px',
                    symbol: L.Symbol.arrowHead({
                        pixelSize: 10,
                        polygon: false,
                        pathOptions: {
                            stroke: true,
                            color: getContrastingColor(seg.severity),
                            weight: 4
                        }
                    })
                }]
            }).addTo(window.map);

            line.bindPopup(`⚠️ Severity: ${seg.severity}`);
        });

        const allCoords = mockData.coloredRouteSegments.flatMap(s => s.coordinates);
        window.map.fitBounds(allCoords);

        return;
    }

    const cacheKey = `routeData_${deliveryId}`;
    const now = Date.now();

    if (!forceRefresh) {
        const cachedRaw = localStorage.getItem(cacheKey);
        if (cachedRaw) {
            try {
                const cached = JSON.parse(cachedRaw);
                if (now - cached.timestamp < 3600000) {
                    console.log("Using cached route data");
                    setupRouteDisplay(cached.data);
                    return;
                } else {
                    console.log("Cached route expired");
                    localStorage.removeItem(cacheKey);
                }
            } catch (e) {
                console.warn("Failed to parse cached route:", e);
                localStorage.removeItem(cacheKey);
            }
        }
    }

    try {
        const response = await fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId);
        const data = await response.json();

        console.log("Data de la SV:", data);

        if (data.error) {
            alert("Error: " + data.error);
            return;
        }

        try {
            // Extragem doar datele esențiale pentru cache
            const reducedData = {
                rawCoordinates: data.rawCoordinates,
                coordinates: data.coordinates,
                stopIndices: data.stopIndices,
                orderIds: data.orderIds,
                segments: (data.segments || []).map(s => ({
                    coordinates: s.coordinates,
                    severity: s.severity
                })),
                failedOrderIds: data.failedOrderIds
            };

            const payload = {
                timestamp: now,
                data: reducedData
            };

            // Încercăm să salvăm în cache
            try {
                localStorage.setItem(cacheKey, JSON.stringify(payload));
            } catch (storageErr) {
                console.warn("LocalStorage overflow – ruta nu a fost salvată în cache:", storageErr);
                localStorage.removeItem(cacheKey);
                // Dacă vrei: fallback la sessionStorage
                // sessionStorage.setItem(cacheKey, JSON.stringify(payload));
            }

        } catch (processingErr) {
            console.error("Eroare la procesarea datelor pentru cache:", processingErr);
        }

        window.failedOrderIds = new Set(data.failedOrderIds || []);
        window.routeResult = data;

        setupRouteDisplay(data);

        console.log("Route data loaded from server");
    } catch (err) {
        console.error("Eroare la preluarea rutei:", err);
    }
}

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
    console.log("[setupRouteDisplay] Starting...");

    console.log("rawCoordinates.length:", (data.rawCoordinates || data.coordinates).length);
    console.log("finalCoordinates.length:", data.coordinates.length);
    console.log("stopIndices:", data.stopIndices);
    console.log("orderIds:", data.orderIds);
    console.log("segmentsData (len):", data.segments.length, data.segments);
    console.log("coloredRouteSegments (len):", data.coloredRouteSegments.length, data.coloredRouteSegments);
    console.log("failedOrderIds:", data.failedOrderIds);

    // Toggle pentru vizualizarea rutei originale si optimizate
    if (!map.getPane('optimizedRoutePane')) {
        map.createPane('optimizedRoutePane');
        map.getPane('optimizedRoutePane').style.zIndex = 450;
    }
    if (!map.getPane('originalRoutePane')) {
        map.createPane('originalRoutePane');
        map.getPane('originalRoutePane').style.zIndex = 440;
    }

    const rawCoords = (data.rawCoordinates || data.coordinates)
        .map(c => [c.latitude, c.longitude]);
    const finalCoords = data.coordinates
        .map(c => [c.latitude, c.longitude]);

    ['initialRouteLayer', 'finalRouteLayer', 'routeControl', 'routeLegend'].forEach(key => {
        if (window[key]) {
            if (key.endsWith('Layer')) map.removeLayer(window[key]);
            else map.removeControl(window[key]);
            window[key] = null;
        }
    });

    window.initialRouteLayer = L.polyline(rawCoords, {
        dashArray: '8,6', color: '#999', weight: 4, opacity: 0.9, pane: 'originalRoutePane'
    });
    window.finalRouteLayer = L.polyline(finalCoords, {
        color: '#0077cc', weight: 5, opacity: 0.9, pane: 'optimizedRoutePane'
    });

    window.layerToggleControl = L.control({ position: 'topright' });
    window.layerToggleControl.onAdd = function (map) {
        const div = L.DomUtil.create('div', 'leaflet-bar leaflet-control leaflet-control-custom');
        div.style.padding = '8px';
        div.style.background = 'white';
        div.style.fontSize = '14px';
        div.innerHTML = `
            <label style="display:flex; align-items:center; cursor:pointer; user-select:none">
              <input type="checkbox" id="toggleOriginal" style="margin-right:6px">
              <span style="
                display:inline-block;
                width:20px;
                height:0;
                border-top:3px dashed #999;
                margin-right:6px;
              "></span>
              Original model
            </label>
            <label style="display:flex; align-items:center; cursor:pointer; user-select:none; margin-top:6px">
              <input type="checkbox" id="toggleOptimized" style="margin-right:6px">
              <span style="
                display:inline-block;
                width:20px;
                height:0;
                border-top:4px solid #0077cc;
                margin-right:6px;
              "></span>
              Optimized model
            </label>
          `;
        return div;
    };

    window.layerToggleControl.addTo(map);

    document.getElementById('toggleOriginal').addEventListener('change', e => {
        if (e.target.checked) initialRouteLayer.addTo(map);
        else map.removeLayer(initialRouteLayer);
    });
    document.getElementById('toggleOptimized').addEventListener('change', e => {
        if (e.target.checked) finalRouteLayer.addTo(map);
        else map.removeLayer(finalRouteLayer);
    });

    document.getElementById('toggleOriginal').checked = false;
    document.getElementById('toggleOptimized').checked = false;

    // Legenda cu severitatea vremii de-a lungul rutei
    window.severityLegend = L.control({ position: 'bottomleft' });
    window.severityLegend.onAdd = function (map) {
        const grades = [0, 0.3, 0.5, 0.7, 0.9];
        const labels = grades.map((g, i) => {
            const from = g;
            const to = grades[i + 1];
            const color = getSeverityColorRGBA(from + 0.05);
            return `
      <i style="display:inline-block;width:18px;height:12px;background:${color};margin-right:6px"></i>
      ${from}${to ? '&ndash;' + to : '+'}
    `;
        });
        const container = L.DomUtil.create('div', 'info legend');
        container.style.background = 'white';
        container.style.padding = '6px';
        container.style.lineHeight = '18px';
        container.innerHTML = `<strong>Severity</strong><br>${labels.join('<br>')}`;
        return container;
    };
    window.severityLegend.addTo(map);

    window.routeCoords = finalCoords;
    window.stopIndices = data.stopIndices;
    window.orderIds = data.orderIds;
    window.segmentsData = data.segments;
    window.coloredRouteSegments = data.coloredRouteSegments;
    window.failedOrderIds = new Set(data.failedOrderIds || []);

    const maxSeg = window.stopIndices.length - 2;

    console.log("[setupRouteDisplay] currentSegment before skip check:", window.currentSegment);
    console.log("[setupRouteDisplay] failedOrderIds =", [...window.failedOrderIds]);
    console.log("[setupRouteDisplay] orderIds =", window.orderIds);

    const manualAdvance = window._justAdvancedManually;
    window._justAdvancedManually = false;

    if (!manualAdvance) {
        // Doar cand nu e un avans manual, sar comanda curenta
        const maxSeg = window.stopIndices.length - 2;

        if (window.currentSegment > maxSeg) {
            console.warn("currentSegment > maxSeg → resetting to maxSeg");
            window.currentSegment = maxSeg;
        }

        while (
            window.currentSegment < window.orderIds.length &&
            window.failedOrderIds.has(window.orderIds[window.currentSegment])
        ) {
            console.warn("Skipping failed orderId:", window.orderIds[window.currentSegment]);
            window.currentSegment++;
        }

        if (window.currentSegment > maxSeg) {
            console.warn("After skipping, currentSegment > maxSeg → resetting to maxSeg");
            window.currentSegment = maxSeg;
        }
    } else {
        console.log("Skipping segment validation (manual advance was just triggered)");
    }

    setupFullscreenControl();
    displayAllSegments();
    //displayColoredRouteSegments(data.coloredRouteSegments);
    displayAvoidPolygons(data.avoidPolygons, data.avoidDescriptions);
    drawOrderMarkers(window.allOrders || []);
    //displayWeatherPolygons(data.coloredRouteSegments);
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

        polygon.bindPopup(`Polygon weather (max value): ${segment.severity}`);
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
            opacity: 1
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
    //if (severity >= 0.9) return '#000000'; // contrast pe mov inchis
    //if (severity >= 0.7) return '#000000'; // contrast pe rosu
    //if (severity >= 0.5) return '#000000'; // contrast pe portocaliu
    //if (severity >= 0.3) return '#000000'; // contrast pe galben
    //return '#000000'; // contrast pe verde
    return '#000000'; // default negru
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
    document.querySelectorAll(".mark-delivered-btn").forEach(button => {
        button.addEventListener("click", function (e) {
            e.preventDefault();

            const form = this.closest("form");
            const formData = new FormData(form);

            const deliveryId = getDeliveryId();

            fetch(form.action, {
                method: form.method,
                body: formData
            })
                .then(response => response.json().catch(() => null))
                .then(data => {
                    if (data && data.success) {
                        console.log("Order marked as delivered.");
                    } else {
                        console.warn("Mark-delivered response not JSON or success=false.");
                    }

                    // Ma mut la segmentul urmator de ruta
                    if (deliveryId) {
                        window._justAdvancedManually = true;
                        advanceRoute();
                    }

                    location.reload();
                })
                .catch(error => {
                    console.error("Error marking as delivered:", error);
                    location.reload();
                });
        });
    });
}

function refreshRoute(deliveryId) {
    console.log("[refreshRoute] Triggered with deliveryId:", deliveryId);

    const cacheKey = `routeData_${deliveryId}`;
    localStorage.removeItem(cacheKey);

    window._justAdvancedManually = true;

    advanceRoute();

    return fetchRouteAndSetup(deliveryId, true)
        .then(() => {
            displayAllSegments();
        })
        .catch(err => console.error("Error fetching updated route", err));
}

// Configurez listener pentru butoanele "Cannot Deliver"
function setupMarkFailedButtons() {
    const deliveryId = getDeliveryId();
    if (!deliveryId) return;

    document.querySelectorAll(".mark-failed-btn").forEach(button => {
        button.addEventListener("click", function (e) {
            e.preventDefault();

            const form = this.closest("form");
            const formData = new FormData(form);

            fetch(form.action, {
                method: form.method,
                body: formData
            })
                .then(response => response.json().catch(() => null))
                .then(data => {
                    if (data && data.success) {
                        console.log("Order marked as undeliverable.");
                    } else {
                        console.warn("Mark-failed response not JSON or success=false.");
                    }

                    if (deliveryId) {
                        window._justAdvancedManually = true;
                        advanceRoute();
                    }

                    location.reload();
                })
                .catch(error => {
                    console.error("Error marking as undeliverable:", error);
                    location.reload();
                });
        });
    });
}

function almostEqual(a, b, epsilon = 0.0001) {
    return Math.abs(a - b) < epsilon;
}

function displayAllSegments() {
    console.log("displayAllSegments");

    if (window.segmentsLayerGroup) {
        window.map.removeLayer(window.segmentsLayerGroup);
    }
    window.segmentsLayerGroup = L.featureGroup().addTo(window.map);

    const stopIndices = window.stopIndices;
    const coords = window.routeCoords;
    const segmentIndex = window.currentSegment;
    const failedSet = new Set(window.failedOrderIds || []);
    const maxSeg = stopIndices.length - 2;

    // Daca am un segment de livrare invalid, sar peste el
    let startIdx, endIdx;
    if (
        segmentIndex === maxSeg &&
        failedSet.has(window.orderIds[segmentIndex - 1])
    ) {
        startIdx = stopIndices[segmentIndex - 1];
        console.log("→ Skipped middle, fusing return: startIdx =", startIdx);
    } else {
        startIdx = stopIndices[segmentIndex];
    }
    endIdx = stopIndices[segmentIndex + 1];

    console.log("  • startIdx:", startIdx, "→ endIdx:", endIdx);

    if (startIdx == null || endIdx == null || startIdx >= coords.length) {
        console.error("Segment indices invalid.");
        return;
    }

    const segmentCoords = coords.slice(startIdx, endIdx + 1);
    if (!segmentCoords.length) {
        console.error("Segment coords empty.");
        return;
    }

    const colored = window.coloredRouteSegments || [];
    let maxSeverity = 0;
    for (let i = startIdx; i < endIdx; i++) {
        const cs = colored[i];
        if (cs && cs.severity > maxSeverity) maxSeverity = cs.severity;
    }
    const segColor = getSeverityColorRGBA(maxSeverity);
    const coloredLine = L.polyline(segmentCoords, {
        color: segColor,
        weight: 6,
        opacity: 1
    }).addTo(window.segmentsLayerGroup);

    L.polylineDecorator(coloredLine, {
        patterns: [{
            offset: '0%',
            repeat: '50px',
            symbol: L.Symbol.arrowHead({
                    pixelSize: 10,
                    polygon: false,
                    pathOptions: {
                        stroke: true,
                        color: getContrastingColor(maxSeverity),
                        weight: 4
                    }
            })
        }]
    }).addTo(window.segmentsLayerGroup);

    let worst = null;
    for (let i = startIdx; i < endIdx; i++) {
        const cs = window.coloredRouteSegments[i];
        if (!cs) continue;
        if (!worst || cs.severity > worst.severity) worst = cs;
    }
    if (worst) {
        const emoji = getWeatherEmoji(worst.weatherCode);
        const desc = getFriendlyWeatherDescription(emoji, worst.weatherDescription);
        const sev = getFormattedSeverity(worst.severity);
        
        coloredLine.bindPopup(`
            <b>⚠️ Severity:</b> ${sev}<br>
            <b>${emoji} Weather:</b> ${desc}
        `);
    }

    window.map.fitBounds(
        window.segmentsLayerGroup.getBounds(),
        { padding: [50, 50] }
    );
}

// Avansez la urmatorul segment din ruta livrarii
function advanceRoute() {
    const maxSeg = window.stopIndices.length - 2;
    console.log("[advanceRoute] currentSegment before advance:", window.currentSegment);
    console.log("[advanceRoute] maxSeg:", maxSeg);

    window.currentSegment = Math.min(window.currentSegment + 1, maxSeg);
    localStorage.setItem('currentSegment_' + getDeliveryId(), window.currentSegment);

    console.log("[advanceRoute] advanced to segment:", window.currentSegment);

    window._justAdvancedManually = true;
    displayAllSegments();
}

// Sterg toate segmentele si decoratorii de pe harta
function clearRouteSegments() {
    return new Promise(resolve => {
        if (window.segmentsLayerGroup) {
            window.map.removeLayer(window.segmentsLayerGroup);
            window.segmentsLayerGroup = null;
        }

        if (window.coloredSegmentsLayerGroup) {
            window.map.removeLayer(window.coloredSegmentsLayerGroup);
            window.coloredSegmentsLayerGroup = null;
        }

        if (window.weatherPolygonsLayerGroup) {
            window.map.removeLayer(window.weatherPolygonsLayerGroup);
            window.weatherPolygonsLayerGroup = null;
        }

        localStorage.setItem("routeCleared", "true");
        console.log("Toate segmentele și decoratorii au fost curățate.");
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
    console.log("Afisez poligoane:", avoidPolygonsData);
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