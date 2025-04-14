document.addEventListener("DOMContentLoaded", function () {
    initApp();
});

// Functia principala de initializare
function initApp() {
    if (isDriver()) {
        // Pentru sofer, incarc comenzile si configurez traseul
        var ordersData = loadOrders();
        if (!ordersData) return;
        var validOrders = filterOrders(ordersData);
        if (validOrders.length === 0) {
            console.error("Nu exista comenzi valide cu date de locatie.");
            return;
        }
        initMap(validOrders);
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
function initMap(validOrders) {
    window.map = L.map('map', { zoomControl: true });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);
    var bounds = L.latLngBounds();
    validOrders.forEach(function (order) {
        var marker = L.marker([order.latitude, order.longitude]).addTo(window.map)
            .bindPopup("<b>Order " + order.id + "</b><br>" + order.address);
        bounds.extend(marker.getLatLng());
    });
    if (validOrders.length > 1) {
        window.map.fitBounds(bounds, { padding: [50, 50] });
    } else {
        window.map.setView([validOrders[0].latitude, validOrders[0].longitude], 14);
    }
}

// Initializez harta pentru non-sofer, cu marker pentru Headquarter si comenzile din zona
function initMapForNonDriver() {
    var hqLat = parseFloat(document.getElementById("headquarterLat").value);
    var hqLng = parseFloat(document.getElementById("headquarterLng").value);
    var hqLatLng = [hqLat, hqLng];

    window.map = L.map('map', { zoomControl: true });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(window.map);
    window.map.setView(hqLatLng, 14);

    // Adaug marker pentru Headquarter
    L.marker(hqLatLng, {
        icon: L.icon({
            iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
            iconSize: [32, 32],
            iconAnchor: [16, 32],
            popupAnchor: [0, -32]
        })
    }).addTo(window.map).bindPopup("<b>Headquarter</b>");

    var ordersData = loadOrders();
    if (ordersData) {
        var validOrders = filterOrders(ordersData);
        validOrders.forEach(function (order) {
            L.marker([order.latitude, order.longitude]).addTo(window.map)
                .bindPopup("<b>Order " + order.id + "</b><br>" + order.address);
        });
        var bounds = L.latLngBounds([hqLatLng]);
        validOrders.forEach(function (order) {
            bounds.extend([order.latitude, order.longitude]);
        });
        window.map.fitBounds(bounds, { padding: [50, 50] });
    }
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
    console.log("Ruta folosita:", data);

    if (MOCK_MODE) {
        window.routeCoords = [
            [44.8600, 24.8650],
            [44.8602, 24.8652],
            [44.8604, 24.8654],
            [44.8606, 24.8656],
            [44.8608, 24.8658]
        ];
        window.stopIndices = [0, 4];
        window.coloredRouteSegments = [
            {
                severity: 0.2,
                coordinates: [
                    [44.8600, 24.8650],
                    [44.8602, 24.8652]
                ]
            },
            {
                severity: 0.4,
                coordinates: [
                    [44.8602, 24.8652],
                    [44.8604, 24.8654]
                ]
            },
            {
                severity: 0.7,
                coordinates: [
                    [44.8604, 24.8654],
                    [44.8606, 24.8656]
                ]
            },
            {
                severity: 0.95,
                coordinates: [
                    [44.8606, 24.8656],
                    [44.8608, 24.8658]
                ]
            }
        ];
        window.orderIds = []; // mock
        window.segmentsData = [{ severity: 0.5 }];
        window.map.setView([44.8604, 24.8654], 16);
    } else {
        window.routeCoords = data.coordinates.map(coord => [coord.latitude, coord.longitude]);
        window.stopIndices = data.stopIndices;
        window.orderIds = data.orderIds;
        window.segmentsData = data.segments;
        window.coloredRouteSegments = data.coloredRouteSegments;
    }

    var headquarterLatLng = window.routeCoords[0];
    L.marker(headquarterLatLng, {
        icon: L.icon({
            iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
            iconSize: [32, 32],
            iconAnchor: [16, 32],
            popupAnchor: [0, -32]
        })
    }).addTo(window.map).bindPopup("<b>Headquarter</b>");

    setupFullscreenControl();

    displayAllSegments();
    displayColoredRouteSegments(window.coloredRouteSegments);
    displayAvoidPolygons(data.avoidPolygons, data.avoidDescriptions);
//    displayWeatherPolygons(window.coloredRouteSegments);
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

    const segmentStart = window.stopIndices[window.currentSegment];
    const segmentEnd = window.stopIndices[window.currentSegment + 1];
    const currentSegmentCoords = window.routeCoords.slice(segmentStart, segmentEnd + 1);

    const coordSet = new Set();
    for (let i = 0; i < currentSegmentCoords.length - 1; i++) {
        const a = currentSegmentCoords[i];
        const b = currentSegmentCoords[i + 1];
        coordSet.add(`${a[0]},${a[1]}|${b[0]},${b[1]}`);
        coordSet.add(`${b[0]},${b[1]}|${a[0]},${a[1]}`);
    }

    coloredSegments.forEach((segment) => {
        const [a, b] = segment.coordinates;
        const key = `${a[0]},${a[1]}|${b[0]},${b[1]}`;
        if (!coordSet.has(key)) return;

        const latlngs = segment.coordinates.map(([lat, lng]) => [lat, lng]);
        const color = getSeverityColorRGBA(segment.severity);
        const emoji = getWeatherEmoji(segment.weatherCode);
        const weatherDesc = (segment.severity === 0 ? "Clear" : (segment.weatherDescription || "unknown"))
            .replace(/\b\w/g, c => c.toUpperCase());

        const polyline = L.polyline(latlngs, {
            color: color,
            weight: 5,
            opacity: 0.7
        });

        polyline.bindPopup(`
            <b>⚠️ Severity:</b> ${segment.severity.toFixed(1)}<br>
            <b>${emoji} Weather:</b> ${weatherDesc}
        `);

        polyline.addTo(window.coloredSegmentsLayerGroup);
    });
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

function getSeverityColorRGBA(severity) {
    if (severity >= 0.9) return 'rgba(188, 65, 250, 0.8)'; // mov
    if (severity >= 0.7) return 'rgba(255, 65, 65, 0.6)'; // rosu
    if (severity >= 0.5) return 'rgba(255, 165, 76, 0.5)'; // portocaliu
    if (severity >= 0.3) return 'rgba(255, 215, 0, 0.9)'; // galben
    return 'rgba(74, 255, 92, 0.3)'; // verde
}

function getColorNameFromRGBA(rgba) {
    switch (rgba) {
        case 'rgba(188, 65, 250, 0.8)': return 'mov';
        case 'rgba(255, 65, 65, 0.6)': return 'roșu';
        case 'rgba(255, 165, 76, 0.5)': return 'portocaliu';
        case 'rgba(255, 215, 0, 0.9)': return 'galben';
        case 'rgba(74, 255, 92, 0.3)': return 'verde';
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
                        console.log("Order marcat ca livrat.");
                    } else {
                        console.error("Eroare la marcarea comenzii ca livrate.");
                    }
                } catch (e) {
                    console.warn("Raspunsul nu este JSON valid, continuam...");
                }
                advanceRoute();
                window.location.reload();
            })
            .catch(function (error) {
                console.error("Eroare la marcarea comenzii ca livrate:", error);
                advanceRoute();
                window.location.reload();
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
    if (window.segmentsLayerGroup) {
        window.map.removeLayer(window.segmentsLayerGroup);
    }

    window.segmentsLayerGroup = L.featureGroup().addTo(window.map);

    for (let i = 0; i < window.stopIndices.length - 1; i++) {
        let startIdx = window.stopIndices[i];
        let endIdx = window.stopIndices[i + 1];
        let segmentCoords = window.routeCoords.slice(startIdx, endIdx + 1);
        let isCurrent = (i === window.currentSegment);
        let segment = window.segmentsData[i];
        if (!segment) {
            console.warn(`Segment ${i} is undefined. Skipping...`);
            continue;
        }

        let polylineStyle;
        if (isCurrent) {
            polylineStyle = {
                color: getSeverityColorRGBA(segment.severity), // doar pt segmentul actual
                weight: 6,
                opacity: 1
            };
        } else {
            polylineStyle = {
                color: '#ADD8E6', // albastru deschis
                weight: 3,
                opacity: 0.6,
                dashArray: '5, 5'
            };
        }

        let polyline = L.polyline(segmentCoords, polylineStyle).addTo(window.segmentsLayerGroup);

        if (isCurrent) {
            L.polylineDecorator(polyline, {
                patterns: [{
                    offset: '0%',
                    repeat: '50px',
                    symbol: L.Symbol.arrowHead({
                        pixelSize: 10,
                        polygon: false,
                        pathOptions: {
                            stroke: true,
                            color: getContrastingColor(segment.severity),
                            weight: 4
                        }
                    })
                }]
            }).addTo(window.segmentsLayerGroup);
        }

        if (isCurrent) {
            const segmentStart = window.stopIndices[i];
            const segmentEnd = window.stopIndices[i + 1];
            const currentSegmentCoords = window.routeCoords.slice(segmentStart, segmentEnd + 1);

            const usedColors = new Set();

            window.coloredRouteSegments.forEach((seg, idx) => {
                const [start, end] = seg.coordinates;

                let matched = false;
                for (let j = 0; j < currentSegmentCoords.length - 1; j++) {
                    const a = currentSegmentCoords[j];
                    const b = currentSegmentCoords[j + 1];

                    const sameSegment =
                        (almostEqual(a[0], start[0]) && almostEqual(a[1], start[1]) &&
                            almostEqual(b[0], end[0]) && almostEqual(b[1], end[1])) ||
                        (almostEqual(a[0], end[0]) && almostEqual(a[1], end[1]) &&
                            almostEqual(b[0], start[0]) && almostEqual(b[1], start[1]));

                    if (sameSegment) {
                        usedColors.add(getSeverityColorRGBA(seg.severity));
                        console.log(
                            `Subsegment ${j}: (${a[0]}, ${a[1]}) -> (${b[0]}, ${b[1]}) | severity=${seg.severity}`
                        );
                        matched = true;
                        break;
                    }
                }
            });

            console.log(
                "Culori folosite pentru segmentul curent:",
                Array.from(usedColors).map(getColorNameFromRGBA)
            );
        }

    }

    window.map.fitBounds(window.segmentsLayerGroup.getBounds(), { padding: [50, 50] });
}

// Avansez la urmatorul segment din ruta livrarii
function advanceRoute() {
    if (window.currentSegment < window.stopIndices.length - 1) {
        window.currentSegment++;
        localStorage.setItem("currentSegment_" + getDeliveryId(), window.currentSegment);
        displayAllSegments();  // Reafisez toate segmentele cu actualizarea curenta
        if (window.currentSegment === window.stopIndices.length - 1) {
            console.log("Segment final afisat: Revenire la HQ.");
        }
    } else {
        console.log("Ruta complet parcurse.");
    }
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