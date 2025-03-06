// Preia valoarea isDriver din input-ul hidden
var isDriver = document.getElementById("isDriver").value === "true";

// Functia initApp se apeleaza la incarcarea paginii
document.addEventListener("DOMContentLoaded", function () {
    initApp();
});

// Daca userul este sofer, configurez functiile de localizare si traseu; altfel, initializez harta doar cu headquarter.
function initApp() {
    if (isDriver) {
        // Pentru sofer, se folosesc tot fluxul
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
        window.currentSegment = parseInt(localStorage.getItem("currentSegment_" + deliveryId)) || 0;
        setupFollowMode();
        fetchRouteAndSetup(deliveryId);
    } else {
        // Pentru non-sofer, pun pe harta doar marker-ul pt Headquarter
        initMapForNonDriver();
    }
}

function loadOrders() {
    var ordersDataElement = document.getElementById("ordersData");
    if (!ordersDataElement) {
        console.error("Nu s-a gasit elementul pentru orders data.");
        return null;
    }
    var ordersData;
    try {
        ordersData = JSON.parse(ordersDataElement.value);
    } catch (e) {
        console.error("Eroare la orders data:", e);
        return null;
    }
    console.log("Orders Data:", ordersData);
    if (!ordersData || ordersData.length === 0) {
        console.error("Nu s-au gasit comenzi pentru aceasta livrare.");
        return null;
    }
    return ordersData;
}

function filterOrders(orders) {
    return orders.filter(function (o) {
        return !isNaN(o.latitude) && !isNaN(o.longitude);
    });
}

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

function initMapForNonDriver() {
    // Iau coordonatele headquarter din input-urile hidden
    var hqLat = parseFloat(document.getElementById("headquarterLat").value);
    var hqLng = parseFloat(document.getElementById("headquarterLng").value);
    var hqLatLng = [hqLat, hqLng];

    // Initializez harta cu centrul pe headquarter
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

    // Incarc si adaug marker-ele pentru comenzi
    var ordersData = loadOrders();
    if (ordersData) {
        var validOrders = filterOrders(ordersData);
        // Adaug fiecare marker pentru comanda
        validOrders.forEach(function (order) {
            L.marker([order.latitude, order.longitude]).addTo(window.map)
                .bindPopup("<b>Order " + order.id + "</b><br>" + order.address);
        });
        // Extind zona vizibila (bounds) pentru a include atat headquarter-ul, cat si marker-ele pentru comenzi
        var bounds = L.latLngBounds([hqLatLng]);
        validOrders.forEach(function (order) {
            bounds.extend([order.latitude, order.longitude]);
        });
        window.map.fitBounds(bounds, { padding: [50, 50] });
    }
}

function getDeliveryId() {
    var deliveryIdElement = document.getElementById("deliveryId");
    if (!deliveryIdElement) {
        console.error("Nu s-a gasit elementul pentru delivery id.");
        return null;
    }
    return deliveryIdElement.value;
}

function setupFollowMode() {
    window.followMode = true;
    window.userMarker = null;
    window.ignoreNextMovestart = false;
    window.refocusButton = document.getElementById("refocusButton");

    function updateRefocusButtonStyle(follow) {
        if (window.refocusButton) {
            if (follow) {
                // Focus true: fundal alb, text albastru
                window.refocusButton.style.backgroundColor = 'white';
                window.refocusButton.style.color = 'blue';
            } else {
                // Focus false: fundal albastru, text alb
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

    // Apelez functia watchUserPosition numai daca userul este sofer
    watchUserPosition();
}

function watchUserPosition() {
    if (!isDriver) return; // Nu cer locatie daca nu este sofer
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
                .bindPopup("Pozitia curenta");
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

function fetchRouteAndSetup(deliveryId) {
    fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId)
        .then(function (response) {
            return response.json();
        })
        .then(function (data) {
            if (data.error) {
                alert("Error: " + data.error);
                return;
            }
            window.routeCoords = data.coordinates.map(function (coord) {
                return [coord.latitude, coord.longitude];
            });
            window.stopIndices = data.stopIndices;
            window.orderIds = data.orderIds;
            console.log("Data ruta:", data);
            console.log("stopIndices:", window.stopIndices);
            console.log("orderIds:", window.orderIds);

            var headquarterLatLng = window.routeCoords[0];
            var hqMarker = L.marker(headquarterLatLng, {
                icon: L.icon({
                    iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
                    iconSize: [32, 32],
                    iconAnchor: [16, 32],
                    popupAnchor: [0, -32]
                })
            }).addTo(window.map).bindPopup("<b>Headquarter</b>");

            setupFullscreenControl();
            displayCurrentSegment();
        })
        .catch(function (error) {
            console.error("Eroare la preluarea rutei:", error);
        });
}

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

function setupMarkDeliveredButtons() {
    document.querySelectorAll(".mark-delivered-btn").forEach(function (button) {
        button.addEventListener("click", function (e) {
            e.preventDefault(); // Previne trimiterea standard a formularului
            var form = this.closest("form");
            var formData = new FormData(form);
            fetch(form.action, {
                method: form.method,
                body: formData
            })
                .then(function (response) {
                    return response.json();
                })
                .then(function (data) {
                    if (data.success) {
                        console.log("Order marcat ca livrat pe server.");
                    } else {
                        console.error("Eroare la marcarea comenzii ca livrate pe server.");
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

function displayCurrentSegment() {
    console.log("stopIndices:", window.stopIndices, "currentSegment:", window.currentSegment);
    if (!window.stopIndices || window.currentSegment >= window.stopIndices.length - 1) {
        console.log("Toate segmentele au fost parcurse.");
        return;
    }
    if (window.currentSegmentLayer) {
        window.map.removeLayer(window.currentSegmentLayer);
        window.map.removeLayer(window.currentDecorator);
    }
    var startIdx = window.stopIndices[window.currentSegment];
    var endIdx = window.stopIndices[window.currentSegment + 1];
    console.log("Afisez segmentul:", window.currentSegment, "de la indexul", startIdx, "la", endIdx);
    var segmentCoords = window.routeCoords.slice(startIdx, endIdx + 1);
    window.currentSegmentLayer = L.polyline(segmentCoords, {
        color: 'red',
        weight: 5,
        opacity: 0.3
    }).addTo(window.map);
    window.currentDecorator = L.polylineDecorator(window.currentSegmentLayer, {
        patterns: [{
            offset: '0%',
            repeat: '50px',
            symbol: L.Symbol.arrowHead({
                pixelSize: 10,
                polygon: false,
                pathOptions: { stroke: true, color: 'blue', weight: 2 }
            })
        }]
    }).addTo(window.map);
    window.map.fitBounds(window.currentSegmentLayer.getBounds(), { padding: [50, 50] });
    if (window.currentSegment === window.stopIndices.length - 2) {
        console.log("Segment final: Revenire la Headquarter.");
    }
}

function advanceRoute() {
    if (window.currentSegment < window.stopIndices.length - 1) {
        window.currentSegment++;
        localStorage.setItem("currentSegment_" + document.getElementById("deliveryId").value, window.currentSegment);
        displayCurrentSegment();
        if (window.currentSegment === window.stopIndices.length - 1) {
            console.log("Segment final afisat: Revenire la HQ.");
        }
    } else {
        console.log("Ruta complet parcurse.");
    }
}

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