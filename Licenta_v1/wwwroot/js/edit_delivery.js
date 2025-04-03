var totalWeight = 1;
var totalVolume = 1;
var map, orderMarkers = {};

const ICON_URLS = {
    selected: "https://cdn-icons-png.flaticon.com/512/1828/1828665.png", // rosu
    normal: "https://cdn-icons-png.flaticon.com/512/1828/1828643.png",   // albastru
};

document.addEventListener("DOMContentLoaded", function () {
    const capacityContainer = document.getElementById("capacity-progress");
    const updateButton = document.getElementById("updateDeliveryButton");
    const selectedOrdersList = document.getElementById("selectedOrdersList");

    totalWeight = parseFloat(capacityContainer.getAttribute("data-total-weight")) || 1;
    totalVolume = parseFloat(capacityContainer.getAttribute("data-total-volume")) || 1;

    // Initializez harta
    map = L.map('map').setView([45.9432, 24.9668], 7);
    map.attributionControl.setPosition('bottomleft');
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    const orderCheckboxes = document.querySelectorAll(".order-checkbox");
    const bounds = L.latLngBounds();

    orderCheckboxes.forEach(cb => {
        const lat = parseFloat(cb.getAttribute("data-latitude"));
        const lng = parseFloat(cb.getAttribute("data-longitude"));
        const orderId = cb.getAttribute("data-id");

        if (!isNaN(lat) && !isNaN(lng)) {
            const iconUrl = cb.checked ? ICON_URLS.selected : ICON_URLS.normal;

            const marker = L.marker([lat, lng], {
                icon: L.icon({ iconUrl, iconSize: [25, 41], iconAnchor: [12, 41] })
            }).addTo(map).bindPopup(() => {
                const cb = document.querySelector(`.order-checkbox[data-id="${orderId}"]`);
                const address = cb.getAttribute("data-address");
                const weight = cb.getAttribute("data-weight");
                const volume = cb.getAttribute("data-volume");
                const isChecked = cb.checked;

                return `
                    <strong>Order #${orderId}</strong><br>
                    ${address}<br>
                    <small>${weight} kg, ${volume} m³</small><br>
                    <button class="btn btn-sm btn-${isChecked ? 'danger' : 'success'} mt-2" onclick="toggleEditOrderSelection('${orderId}')">
                        ${isChecked ? 'Deselect' : 'Select'}
                    </button>
                `;
            });

            orderMarkers[orderId] = marker;
            bounds.extend(marker.getLatLng());
        }
    });

    if (Object.keys(orderMarkers).length > 0) {
        map.fitBounds(bounds, { padding: [50, 50] });
    }

    function updateMapMarkers() {
        const selected = new Set();
        document.querySelectorAll(".order-checkbox:checked").forEach(cb => selected.add(cb.getAttribute("data-id")));

        Object.entries(orderMarkers).forEach(([id, marker]) => {
            const cb = document.querySelector(`.order-checkbox[data-id="${id}"]`);
            const iconUrl = selected.has(id) ? ICON_URLS.selected : ICON_URLS.normal;
            const icon = L.icon({ iconUrl, iconSize: [25, 41], iconAnchor: [12, 41] });
            marker.setIcon(icon);
        });
    }

    function updateCapacityVisuals() {
        let usedWeight = 0, usedVolume = 0;
        document.querySelectorAll(".order-checkbox:checked").forEach(cb => {
            usedWeight += parseFloat(cb.getAttribute("data-weight")) || 0;
            usedVolume += parseFloat(cb.getAttribute("data-volume")) || 0;
        });

        const weightPerc = Math.min((usedWeight / totalWeight) * 100, 100);
        const volumePerc = Math.min((usedVolume / totalVolume) * 100, 100);

        const weightBar = document.getElementById("weightProgress");
        const volumeBar = document.getElementById("volumeProgress");

        weightBar.style.width = weightPerc + "%";
        volumeBar.style.width = volumePerc + "%";

        document.getElementById("weightValue").innerText = `${usedWeight.toFixed(1)} / ${totalWeight} kg`;
        document.getElementById("volumeValue").innerText = `${usedVolume.toFixed(1)} / ${totalVolume} m³`;

        weightBar.classList.toggle("bg-danger", usedWeight > totalWeight);
        volumeBar.classList.toggle("bg-danger", usedVolume > totalVolume);

        updateButton.disabled = (usedWeight > totalWeight || usedVolume > totalVolume);

        // Lista ce contine comenzile selectate
        selectedOrdersList.innerHTML = "";

        document.querySelectorAll(".order-checkbox:checked").forEach(cb => {
            const id = cb.getAttribute("data-id");
            const addr = cb.getAttribute("data-address");
            const w = cb.getAttribute("data-weight");
            const v = cb.getAttribute("data-volume");
            const isExisting = cb.name === "keepOrderIds";

            const container = document.createElement("div");
            container.className = "d-flex justify-content-between align-items-start border rounded p-1 bg-light";
            container.innerHTML = `
                <input type="hidden" name="${cb.name}" value="${id}" />
                <div class="pe-2">
                    <strong>Order #${id}</strong><br>
                    <small>${addr}</small><br>
                    <small>${w} kg, ${v} m³</small>
                </div>
                <button type="button" class="btn-close ms-2" aria-label="Remove" data-id="${id}"></button>
            `;
            selectedOrdersList.appendChild(container);
        });

        // Stergerea comenzilor selectate
        document.querySelectorAll("#selectedOrdersList .btn-close").forEach(btn => {
            btn.addEventListener("click", () => {
                const idToRemove = btn.getAttribute("data-id");
                const checkbox = document.querySelector(`.order-checkbox[data-id="${idToRemove}"]`);
                if (checkbox) checkbox.checked = false;
                updateCapacityVisuals();
            });
        });

        const wrapper = document.getElementById("selectedOrdersListWrapper");
        const placeholder = document.getElementById("selectedOrdersListPlaceholder");
        const selectedCount = selectedOrdersList.querySelectorAll(".d-flex").length;

        placeholder.classList.toggle("d-none", selectedCount > 0);

        requestAnimationFrame(() => {
            const firstCard = selectedOrdersList.querySelector("div");
            if (firstCard) {
                const cardHeight = firstCard.offsetHeight;
                selectedOrdersList.style.maxHeight = `${cardHeight}px`;
                wrapper.style.height = `${cardHeight + 55}px`;
            } else {
                wrapper.style.height = `${70 + 55}px`;
                selectedOrdersList.style.maxHeight = "none";
            }
        });

        updateMapMarkers();
    }

    orderCheckboxes.forEach(cb => {
        cb.addEventListener("change", updateCapacityVisuals);
    });

    updateCapacityVisuals();
});

window.toggleEditOrderSelection = function (orderId) {
    const cb = document.querySelector(`.order-checkbox[data-id="${orderId}"]`);
    const marker = orderMarkers[orderId];
    if (!cb || !marker) return;

    cb.checked = !cb.checked;
    cb.dispatchEvent(new Event("change"));

    marker.closePopup();
};
