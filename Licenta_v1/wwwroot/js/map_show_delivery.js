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
    fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId)
        .then(response => response.json())
        .then(data => {
            if (data.error) {
                alert("Error: " + data.error);
                return;
            }

            console.log("Ruta primită de la backend:", data);
            window.routeCoords = data.coordinates.map(coord => [coord.latitude, coord.longitude]);
            window.stopIndices = data.stopIndices;
            window.orderIds = data.orderIds;
            window.segmentsData = data.segments; // Salvez segmentele pentru acces ulterior

            console.log("Coordonate ruta:", window.routeCoords);
            console.log("stopIndices:", window.stopIndices);
            console.log("orderIds:", window.orderIds);
            console.log("Date segmente:", window.segmentsData);

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
            displayDangerPolygons(data.dangerPolygons);

            function displayDangerPolygons(dangerPolygons) {
                if (!dangerPolygons || dangerPolygons.length === 0) return;

                dangerPolygons.forEach(polyCoords => {
                    L.polygon(polyCoords.map(c => [c[1], c[0]]), {
                        color: 'red',
                        fillOpacity: 0.3
                    }).addTo(window.map);
                });
            }
        })
        .catch(error => {
            console.error("Eroare la preluarea rutei:", error);
        });
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
        let segment = window.segmentsData[i];  // Date segment primit de la backend

        console.log(`Segment ${i + 1}:`, segment);

        let polylineStyle = isCurrent
            ? { color: 'blue', weight: 6, opacity: 1 }
            : segment.isWeatherDangerous
                ? { color: 'orange', weight: 5, opacity: 0.8, dashArray: '10, 5' }
                : { color: '#ADD8E6', weight: 3, opacity: 0.6, dashArray: '5, 5' };

        let polyline = L.polyline(segmentCoords, polylineStyle).addTo(window.segmentsLayerGroup);

        if (isCurrent) {
            L.polylineDecorator(polyline, {
                patterns: [{
                    offset: '0%',
                    repeat: '50px',
                    symbol: L.Symbol.arrowHead({
                        pixelSize: 10,
                        polygon: false,
                        pathOptions: { stroke: true, color: 'aqua', weight: 2 }
                    })
                }]
            }).addTo(window.segmentsLayerGroup);
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
