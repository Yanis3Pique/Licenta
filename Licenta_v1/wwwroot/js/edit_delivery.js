var map, orderMarkers = {};

document.addEventListener("DOMContentLoaded", function () {
    // Iau containerul cu progresele si valorile totale
    var progressContainer = document.getElementById("capacity-progress");
    if (!progressContainer) return;
    var totalWeight = parseFloat(progressContainer.getAttribute("data-total-weight")) || 1;
    var totalVolume = parseFloat(progressContainer.getAttribute("data-total-volume")) || 1;
    var updateButton = document.getElementById("updateDeliveryButton");

    // Initializez harta
    map = L.map('map').setView([45.9432, 24.9668], 7);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Iau toate checkboxurile cu date de latitudine si longitudine
    var orderCheckboxes = document.querySelectorAll("input.form-check-input[data-latitude][data-longitude]");
    var bounds = L.latLngBounds();

    // Creez markeri pentru fiecare comanda si ii stochez in orderMarkers
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

    // Daca am markeri, ajustez harta sa fie vizibili toti markerii
    if (Object.keys(orderMarkers).length > 0) {
        map.fitBounds(bounds, { padding: [50, 50] });
    }

    // Functie care actualizeaza markerii cand se schimba starea checkboxurilor
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

    // Functie care actualizeaza vizualizarea capacitatii si apoi markerii de pe harta
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
        var weightProgress = document.getElementById("weightProgress");
        var volumeProgress = document.getElementById("volumeProgress");
        var weightValue = document.getElementById("weightValue");
        var volumeValue = document.getElementById("volumeValue");

        if (weightProgress) {
            weightProgress.style.width = weightPerc + "%";
            weightProgress.setAttribute("aria-valuenow", usedWeight);
            if (usedWeight > totalWeight) {
                weightProgress.classList.remove("bg-primary");
                weightProgress.classList.add("bg-danger");
            } else {
                weightProgress.classList.remove("bg-danger");
                weightProgress.classList.add("bg-primary");
            }
        }

        if (volumeProgress) {
            volumeProgress.style.width = volumePerc + "%";
            volumeProgress.setAttribute("aria-valuenow", usedVolume);
            if (usedVolume > totalVolume) {
                volumeProgress.classList.remove("bg-info");
                volumeProgress.classList.add("bg-danger");
            } else {
                volumeProgress.classList.remove("bg-danger");
                volumeProgress.classList.add("bg-info");
            }
        }

        if (weightValue) {
            if (usedWeight > totalWeight) {
                weightValue.innerText = usedWeight.toFixed(1) + " / " + totalWeight + " kg - Exceeds capacity!";
                weightValue.style.color = "red";
            } else {
                weightValue.innerText = usedWeight.toFixed(1) + " / " + totalWeight + " kg";
                weightValue.style.color = "";
            }
        }
        if (volumeValue) {
            if (usedVolume > totalVolume) {
                volumeValue.innerText = usedVolume.toFixed(1) + " / " + totalVolume + " m³ - Exceeds capacity!";
                volumeValue.style.color = "red";
            } else {
                volumeValue.innerText = usedVolume.toFixed(1) + " / " + totalVolume + " m³";
                volumeValue.style.color = "";
            }
        }

        if (updateButton) {
            updateButton.disabled = (usedWeight > totalWeight || usedVolume > totalVolume);
        }

        updateMapMarkers();
    }

    // Evenimentul reprezinta selectarea sau deselectarea unei comenzi(box)
    orderCheckboxes.forEach(function (checkbox) {
        checkbox.addEventListener("change", updateCapacityVisuals);
    });

    updateCapacityVisuals();
});
