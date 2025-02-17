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

    // Iau ruta optima
    fetch('/Deliveries/GetOptimalRoute?id=' + deliveryId)
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

            window.stopIndices = data.stopIndices; // Salvez indicii de stop pt afisarea segmentului curent din livrare

            console.log("data: ", data);
            console.log("window.stopIndices: ", window.stopIndices);

            // Pun pe harta locatia Headquarter-ului
            var headquarterLatLng = window.routeCoords[0];

            var hqMarker = L.marker(headquarterLatLng, {
                icon: L.icon({
                    iconUrl: 'https://cdn-icons-png.flaticon.com/512/684/684908.png',
                    iconSize: [32, 32],
                    iconAnchor: [16, 32],
                    popupAnchor: [0, -32]
                })
            }).addTo(window.map).bindPopup("<b>Headquarter</b>");

            bounds.extend(hqMarker.getLatLng()); // Fac harta mai mare ca sa intre si Headquarter-ul

            displayCurrentSegment(); // Afisez segmentul curent din livrare

            // Populez tabelul cu informatii despre fiecare comanda din livrare
            if (data.segments && data.segments.length > 0) {
                var tbody = document.getElementById("routeSegments");
                tbody.innerHTML = "";

                let cumulativeDistance = 0;
                let cumulativeDuration = 0;

                data.segments.forEach(function (segment, index) {
                    var tr = document.createElement("tr");
                    var tdLabel = document.createElement("td");
                    tdLabel.setAttribute("id", "light-blue-elements-background-id");

                    console.log("index: ", index, " - data.segments.length: ", data.segments.length - 1);

                    // Valori cumulative pt tabel
                    cumulativeDistance += segment.distance;
                    cumulativeDuration += segment.duration;

                    if (index === data.segments.length - 1) {
                        tdLabel.textContent = "Return to Headquarter";
                    } else {
                        tdLabel.textContent = "Order " + data.orderIds[index];
                    }

                    var tdDistance = document.createElement("td");
                    tdDistance.setAttribute("id", "light-blue-elements-background-id");
                    tdDistance.textContent = cumulativeDistance < 1000
                        ? cumulativeDistance.toFixed(0) + " m"
                        : (cumulativeDistance / 1000).toFixed(1) + " km";

                    var tdTime = document.createElement("td");
                    tdTime.setAttribute("id", "light-blue-elements-background-id");

                    if (cumulativeDuration < 60) {
                        tdTime.textContent = cumulativeDuration.toFixed(0) + " sec";
                    } else if (cumulativeDuration < 3600) {
                        tdTime.textContent = (cumulativeDuration / 60).toFixed(0) + " min";
                    } else {
                        var hours = Math.floor(cumulativeDuration / 3600);
                        var minutes = Math.floor((cumulativeDuration % 3600) / 60);
                        tdTime.textContent = hours + " h " + (minutes > 0 ? minutes + " min" : "");
                    }

                    tr.appendChild(tdLabel);
                    tr.appendChild(tdDistance);
                    tr.appendChild(tdTime);
                    tbody.appendChild(tr);
                });
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
        console.log("All segments completed.");
        return;
    }

    // Sterg segmentul anterior de pe harta
    if (window.currentSegmentLayer) {
        window.map.removeLayer(window.currentSegmentLayer);
        window.map.removeLayer(window.currentDecorator);
    }

    var startIdx = window.stopIndices[window.currentSegment];
    var endIdx = window.stopIndices[window.currentSegment + 1];

    console.log("Displaying segment:", window.currentSegment, "from index", startIdx, "to", endIdx);

    var segmentCoords = window.routeCoords.slice(startIdx, endIdx + 1);

    // Desenez segmentul curent
    window.currentSegmentLayer = L.polyline(segmentCoords, {
        color: 'red',
        weight: 5,
        opacity: 0.3
    }).addTo(window.map);

    // Adaug sagetile directionale pe liniile segmentului
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

    // Ultimul segment(intoarcerea la HQ)
    if (window.currentSegment === window.stopIndices.length - 2) {
        console.log("Final segment: Returning to headquarters.");
    }
}

// Trecerea la urmatorul segment
function advanceRoute() {
    if (window.currentSegment < window.stopIndices.length - 1) {
        window.currentSegment++;
        localStorage.setItem("currentSegment_" + document.getElementById("deliveryId").value, window.currentSegment);
        displayCurrentSegment();

        if (window.currentSegment === window.stopIndices.length - 1) {
            console.log("Final route segment displayed: Returning to HQ.");
        }
    } else {
        console.log("Route completed.");
    }
}

// Fac bind pe butoanele de "Mark as Delivered" ca sa apeleze advanceRoute()
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".mark-delivered-btn").forEach(function (button) {
        button.addEventListener("click", function () {
            advanceRoute();
        });
    });
});

// Sterg segmentul de final de pe harta(cand se termina livrarea cu totul)
function clearRouteSegments() {
    return new Promise((resolve) => {
        if (window.currentSegmentLayer) { // liniile
            window.map.removeLayer(window.currentSegmentLayer);
        }

        if (window.currentDecorator) { // si decoratorii
            window.map.removeLayer(window.currentDecorator);
        }

        // se sterg cu totul
        window.map.eachLayer(function (layer) {
            if (layer instanceof L.Polyline && !(layer instanceof L.Marker)) {
                window.map.removeLayer(layer);
            } else if (layer instanceof L.LayerGroup) {
                window.map.removeLayer(layer);
            }
        });

        // Salvez starea in localStorage
        localStorage.setItem("routeCleared", "true");

        console.log("All route segments and decorators removed. Only orders and headquarters remain.");
        resolve(); // Ma asigur ca se executa functia inainte sa trec mai departe
    });
}

// Verific ca livrarea e completa
function checkDeliveryStatus() {
    var deliveryStatusElement = document.getElementById("deliveryStatus");
    if (!deliveryStatusElement) {
        console.error("No delivery status element found.");
        return;
    }

    var deliveryStatus = deliveryStatusElement.value.trim();
    console.log("Current Delivery Status:", deliveryStatus);

    if (deliveryStatus === "Completed") {
        console.log("Delivery is completed. Waiting for map initialization...");

        // Astept sa se creeze harta pana sa sterg rutele
        var mapCheckInterval = setInterval(function () {
            if (window.map) {
                clearRouteSegments();
                clearInterval(mapCheckInterval); // Dupa ce harta e creata ma opresc
            }
        }, 200);
    }
}

// Ma asigur ca functia ruleaza dupa ce s-au incarcat DOM-ul si harta
document.addEventListener("DOMContentLoaded", function () {
    setTimeout(() => {
        checkDeliveryStatus();
    }, 500); // Delay pt crearea hartii
});
