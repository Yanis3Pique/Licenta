document.addEventListener("DOMContentLoaded", function () {
    // Iau comenzile din input-ul hidden din View-ul Show
    var ordersDataElement = document.getElementById("ordersData");
    if (!ordersDataElement) {
        console.error("No orders data element found.");
        return;
    }

    var ordersData;
    try {
        ordersData = JSON.parse(ordersDataElement.value);
    } catch (e) {
        console.error("Error parsing orders data:", e);
        return;
    }
    console.log("Orders Data:", ordersData);

    if (!ordersData || ordersData.length === 0) {
        console.error("No orders found for this delivery.");
        return;
    }

    // Filtrez comenzile sa contina latitudine si longitudine
    var validOrders = ordersData.filter(function (o) {
        return !isNaN(o.latitude) && !isNaN(o.longitude);
    });
    if (validOrders.length === 0) {
        console.error("No valid orders with location data.");
        return;
    }

    // Initializez harta
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

    // Iau deliveryId-ul din input-ul hidden din View-ul Show
    var deliveryIdElement = document.getElementById("deliveryId");
    if (!deliveryIdElement) {
        console.error("No delivery id element found.");
        return;
    }
    var deliveryId = deliveryIdElement.value;

    // Iau segmentul curent din localStorage ca sa stiu unde am ramas
    window.currentSegment = parseInt(localStorage.getItem("currentSegment_" + deliveryId)) || 0;

    // Iau ruta optima de la server (folosind metoda din controller)
    fetch('/Deliveries/GetOptimalRoute?deliveryId=' + deliveryId)
        .then(function (response) {
            return response.json();
        })
        .then(function (data) {
            if (data.error) {
                alert("Error: " + data.error);
                return;
            }

            // Convertesc coordonatele rutei pt Leaflet
            window.routeCoords = data.coordinates.map(function (coord) {
                return [coord.latitude, coord.longitude];
            });

            // Salvez indicii de stop pt afisarea segmentelor si orderIds pentru referinta
            window.stopIndices = data.stopIndices;
            window.orderIds = data.orderIds;

            console.log("Data ruta:", data);
            console.log("stopIndices:", window.stopIndices);
            console.log("orderIds:", window.orderIds);

            // Pun pe harta locatia Headquarter-ului (prima coordonata)
            var headquarterLatLng = window.routeCoords[0];
            var hqMarker = L.marker(headquarterLatLng, {
                icon: L.icon({
                    iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
                    iconSize: [32, 32],
                    iconAnchor: [16, 32],
                    popupAnchor: [0, -32]
                })
            }).addTo(window.map).bindPopup("<b>Headquarter</b>");
            bounds.extend(hqMarker.getLatLng()); // Extind harta sa includa HQ

            var carIcon = L.icon({
                iconUrl: '/Images/car.png', // URL iconita masina
                iconSize: [32, 32],
                iconAnchor: [16, 16]
            });
            var userMarker; // Marker-ul pentru pozitia curenta a userului

            // Functie care incearca sa obtina pozitia userului continuu
            function watchUserPosition() {
                navigator.geolocation.watchPosition(function (position) {
                    console.log("Pozitia - ", position);
                    var lat = position.coords.latitude;
                    var lng = position.coords.longitude;
                    var latlng = [lat, lng];
                    if (!userMarker) {
                        userMarker = L.marker(latlng, { icon: carIcon, rotationAngle: 0 }).addTo(window.map)
                            .bindPopup("My current position");
                    } else {
                        userMarker.setLatLng(latlng);
                    }
                    // Centreaza harta pe pozitia userului
                    window.map.panTo(latlng, { animate: true });
                }, function (error) {
                    if (error.code === error.PERMISSION_DENIED) {
                        alert("Please accept GPS tracking in order to see your location on the map.");
                    } else {
                        console.error("Error obtaining GPS location:", error);
                    }
                }, { enableHighAccuracy: true });
            }

            // Daca geolocatia este suportata, incerc sa obtin pozitia userului
            if ("geolocation" in navigator) {
                watchUserPosition();
            } else {
                console.error("Geolocation not supported on this browser.");
            }

            displayCurrentSegment(); // Afisez segmentul curent din livrare

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
            function toggleFullScreen() {
                var mapContainer = document.getElementById('map-container');
                if (!document.fullscreenElement) {
                    mapContainer.requestFullscreen().catch(function (err) {
                        alert("Eroare la activarea modului full screen: " + err.message);
                    });
                } else {
                    document.exitFullscreen();
                }
            }
            L.control.fullscreen().addTo(window.map);

            // Pentru telefoane(rotatia/giroscopul)
            if (typeof DeviceOrientationEvent.requestPermission === 'function') {
                DeviceOrientationEvent.requestPermission()
                    .then(function (response) {
                        if (response === 'granted') {
                            window.addEventListener('deviceorientation', handleOrientation);
                        }
                    })
                    .catch(console.error);
            } else {
                window.addEventListener('deviceorientation', handleOrientation);
            }

            function handleOrientation(event) {
                // event.alpha ne returneaza heading-ul compasului telefonului (intre 0-360 de grade)
                var heading = event.alpha;
                if (userMarker && userMarker.setRotationAngle) {
                    userMarker.setRotationAngle(heading);
                }
            }
        })
        .catch(function (error) {
            console.error("Error fetching route:", error);
        });
});

// Aratam segmentul curent din livrare
function displayCurrentSegment() {
    console.log("stopIndices:", window.stopIndices, "currentSegment:", window.currentSegment);

    if (!window.stopIndices || window.currentSegment >= window.stopIndices.length - 1) {
        console.log("Toate segmentele au fost parcurse.");
        return;
    }

    // Sterg segmentul anterior de pe harta
    if (window.currentSegmentLayer) {
        window.map.removeLayer(window.currentSegmentLayer);
        window.map.removeLayer(window.currentDecorator);
    }

    var startIdx = window.stopIndices[window.currentSegment];
    var endIdx = window.stopIndices[window.currentSegment + 1];

    console.log("Afisez segmentul:", window.currentSegment, "de la indexul", startIdx, "la", endIdx);

    var segmentCoords = window.routeCoords.slice(startIdx, endIdx + 1);

    // Desenez segmentul curent
    window.currentSegmentLayer = L.polyline(segmentCoords, {
        color: 'red',
        weight: 5,
        opacity: 0.3
    }).addTo(window.map);

    // Adaug sagetile directionale pe linia segmentului
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

    // Daca este ultimul segment (revenirea la HQ)
    if (window.currentSegment === window.stopIndices.length - 2) {
        console.log("Segment final: Revenire la Headquarter.");
    }
}

// Functie pentru trecerea la urmatorul segment
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

// Leg butoanele de "Mark as Delivered" pentru a apela advanceRoute() si pentru a trimite cererea catre server
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".mark-delivered-btn").forEach(function (button) {
        button.addEventListener("click", function (e) {
            e.preventDefault(); // Previne trimiterea standard a formularului

            // Obține formularul în care se află butonul
            var form = this.closest("form");
            var formData = new FormData(form);

            // Trimite formularul către controller folosind fetch (AJAX)
            fetch(form.action, {
                method: form.method,
                body: formData
            })
                .then(function (response) {
                    return response.json();
                })
                .then(function (data) {
                    if (data.success) {
                        console.log("Order marked as delivered on server.");
                    } else {
                        console.error("Server returned an error marking order as delivered.");
                    }
                    // Apeleaza functia pentru afisarea urmatorului segment
                    advanceRoute();
                    window.location.reload();
                })
                .catch(function (error) {
                    console.error("Error marking order delivered:", error);
                    advanceRoute();
                    window.location.reload();
                });
        });
    });
});

// Sterg segmentul final de pe harta cand livrarea este completa
function clearRouteSegments() {
    return new Promise((resolve) => {
        if (window.currentSegmentLayer) { // sterg linia
            window.map.removeLayer(window.currentSegmentLayer);
        }

        if (window.currentDecorator) { // sterg decoratorul
            window.map.removeLayer(window.currentDecorator);
        }

        // Sterg toate celelalte polilinii (cu exceptia marker-elor)
        window.map.eachLayer(function (layer) {
            if (layer instanceof L.Polyline && !(layer instanceof L.Marker)) {
                window.map.removeLayer(layer);
            } else if (layer instanceof L.LayerGroup) {
                window.map.removeLayer(layer);
            }
        });

        // Salvez starea in localStorage
        localStorage.setItem("routeCleared", "true");

        console.log("Toate segmentele si decoratorii au fost sterse. Raman doar comenzile si headquarter-ul.");
        resolve(); // Asigur ca functia se executa complet inainte de urmatoarea actiune
    });
}

// Verific daca livrarea este completa
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

        // Astept ca harta sa se incarce inainte de a sterge ruta
        var mapCheckInterval = setInterval(function () {
            if (window.map) {
                clearRouteSegments();
                clearInterval(mapCheckInterval);
            }
        }, 200);
    }
}

// Asigur ca functia ruleaza dupa incarcarea DOM-ului si a hartii
document.addEventListener("DOMContentLoaded", function () {
    setTimeout(function () {
        checkDeliveryStatus();
    }, 500); // Delay pentru incarcarea hartii
});