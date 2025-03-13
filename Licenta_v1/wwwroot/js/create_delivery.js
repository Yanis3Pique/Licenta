var totalWeight = 1;
var totalVolume = 1;
var map, orderMarkers = {};

document.addEventListener("DOMContentLoaded", function () {
    var capacityContainer = document.getElementById("capacity-progress");
    var vehicleSelect = document.getElementById("vehicleSelect");
    var createButton = document.getElementById("createDeliveryButton");

    // Initializez harta
    map = L.map('map').setView([45.9432, 24.9668], 7);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Creez markeri pentru fiecare comanda o singura data si ii stochez in orderMarkers
    var orderCheckboxes = document.querySelectorAll(".order-checkbox");
    var bounds = L.latLngBounds();
    orderCheckboxes.forEach(function (checkbox) {
        var lat = parseFloat(checkbox.getAttribute("data-latitude"));
        var lng = parseFloat(checkbox.getAttribute("data-longitude"));
        if (!isNaN(lat) && !isNaN(lng)) {
            var iconUrl = checkbox.checked
                ? "https://cdn-icons-png.flaticon.com/512/1828/1828665.png" // rosu pentru comenzi selectate
                : "https://cdn-icons-png.flaticon.com/512/1828/1828643.png"; // albastru pentru comenzi neselectate
            var icon = L.icon({
                iconUrl: iconUrl,
                iconSize: [25, 41],
                iconAnchor: [12, 41]
            });
            var marker = L.marker([lat, lng], { icon: icon })
                .addTo(map)
                .bindPopup("Order #" + checkbox.value);
            orderMarkers[checkbox.value] = marker;
            bounds.extend(marker.getLatLng());
        }
    });
    if (Object.keys(orderMarkers).length > 0) {
        map.fitBounds(bounds, { padding: [50, 50] });
    }

    // Marchez cu rosu comanda selectata si cu albastru comanda neselectata
    function updateMapMarkers() {
        orderCheckboxes.forEach(function (checkbox) {
            var marker = orderMarkers[checkbox.value];
            if (marker) {
                var newIconUrl = checkbox.checked
                    ? "https://cdn-icons-png.flaticon.com/512/1828/1828665.png"
                    : "https://cdn-icons-png.flaticon.com/512/1828/1828643.png";
                var newIcon = L.icon({
                    iconUrl: newIconUrl,
                    iconSize: [25, 41],
                    iconAnchor: [12, 41]
                });
                marker.setIcon(newIcon);
            }
        });
    }

    // Dau refresh la progress bar si la valorile afisate
    function updateCapacityVisuals() {
        var usedWeight = 0, usedVolume = 0;
        orderCheckboxes.forEach(function (checkbox) {
            if (checkbox.checked) {
                usedWeight += parseFloat(checkbox.getAttribute("data-weight")) || 0;
                usedVolume += parseFloat(checkbox.getAttribute("data-volume")) || 0;
            }
        });
        var weightPerc = Math.min((usedWeight / totalWeight) * 100, 100);
        var volumePerc = Math.min((usedVolume / totalVolume) * 100, 100);
        var weightProgressElem = document.getElementById("weightProgress");
        var volumeProgressElem = document.getElementById("volumeProgress");

        weightProgressElem.style.width = weightPerc + "%";
        volumeProgressElem.style.width = volumePerc + "%";
        document.getElementById("weightValue").innerText = usedWeight.toFixed(1) + " / " + totalWeight + " kg";
        document.getElementById("volumeValue").innerText = usedVolume.toFixed(1) + " / " + totalVolume + " m³";

        // Schimb culoarea progress bar-ului in rosu daca capacitatea este depasita
        if (usedWeight > totalWeight) {
            weightProgressElem.classList.remove("bg-primary");
            weightProgressElem.classList.add("bg-danger");
        } else {
            weightProgressElem.classList.remove("bg-danger");
            weightProgressElem.classList.add("bg-primary");
        }

        if (usedVolume > totalVolume) {
            volumeProgressElem.classList.remove("bg-info");
            volumeProgressElem.classList.add("bg-danger");
        } else {
            volumeProgressElem.classList.remove("bg-danger");
            volumeProgressElem.classList.add("bg-info");
        }

        // Dau disable la butonul de creare delivery daca capacitatea este depasita
        createButton.disabled = (usedWeight > totalWeight || usedVolume > totalVolume);
        updateMapMarkers();
    }

    // Actualizez capacitatatile in functie de vehiculul selectat
    if (vehicleSelect) {
        var selectedOption = vehicleSelect.options[vehicleSelect.selectedIndex];
        var newTotalWeight = selectedOption.getAttribute("data-maxweight");
        var newTotalVolume = selectedOption.getAttribute("data-maxvolume");
        capacityContainer.setAttribute("data-total-weight", newTotalWeight);
        capacityContainer.setAttribute("data-total-volume", newTotalVolume);
        totalWeight = parseFloat(newTotalWeight) || 1;
        totalVolume = parseFloat(newTotalVolume) || 1;
        document.getElementById("weightValue").innerText = "0 / " + newTotalWeight + " kg";
        document.getElementById("volumeValue").innerText = "0 / " + newTotalVolume + " m³";
    }

    // Modific capacitatile cans se selecteaza un alt vehicul
    if (vehicleSelect) {
        vehicleSelect.addEventListener("change", function () {
            var selectedOption = vehicleSelect.options[vehicleSelect.selectedIndex];
            var newTotalWeight = selectedOption.getAttribute("data-maxweight");
            var newTotalVolume = selectedOption.getAttribute("data-maxvolume");
            capacityContainer.setAttribute("data-total-weight", newTotalWeight);
            capacityContainer.setAttribute("data-total-volume", newTotalVolume);
            totalWeight = parseFloat(newTotalWeight) || 1;
            totalVolume = parseFloat(newTotalVolume) || 1;
            document.getElementById("weightValue").innerText = "0 / " + newTotalWeight + " kg";
            document.getElementById("volumeValue").innerText = "0 / " + newTotalVolume + " m³";
            updateCapacityVisuals();
        });
    }

    // Evenimentul reprezinta selectarea sau deselectarea unei comenzi(box)
    orderCheckboxes.forEach(function (checkbox) {
        checkbox.addEventListener("change", updateCapacityVisuals);
    });
    updateCapacityVisuals();
});
