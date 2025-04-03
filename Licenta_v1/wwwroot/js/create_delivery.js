var totalWeight = 1;
var totalVolume = 1;
var map, orderMarkers = {};

const ICON_URLS = {
    selected: "https://cdn-icons-png.flaticon.com/512/1828/1828665.png",
    normal: "https://cdn-icons-png.flaticon.com/512/1828/1828643.png",
    restricted: "https://cdn-icons-png.flaticon.com/512/8213/8213126.png"
};

document.addEventListener("DOMContentLoaded", function () {
    const vehicleIdInput = document.getElementById("selectedVehicleId");
    const selectedOrdersList = document.getElementById("selectedOrdersList");
    const createButton = document.getElementById("createDeliveryButton");

    // Handler pentru selectarea vehiculului
    document.querySelectorAll(".vehicle-option").forEach(button => {
        button.addEventListener("click", () => {
            const vehicleId = button.getAttribute("data-id");
            const label = button.getAttribute("data-name");
            const maxWeight = parseFloat(button.getAttribute("data-maxweight")) || 1;
            const maxVolume = parseFloat(button.getAttribute("data-maxvolume")) || 1;

            totalWeight = maxWeight;
            totalVolume = maxVolume;

            vehicleIdInput.value = vehicleId;
            document.getElementById("selectedVehicleDisplay").innerText = label;
            document.getElementById("weightValue").innerText = `0 / ${totalWeight} kg`;
            document.getElementById("volumeValue").innerText = `0 / ${totalVolume} m³`;

            updateOrderAvailability();
        });
    });

    // Handler pentru selectarea soferului
    document.querySelectorAll(".driver-option").forEach(button => {
        button.addEventListener("click", () => {
            const driverId = button.getAttribute("data-id");
            const name = button.getAttribute("data-name");

            document.getElementById("selectedDriverId").value = driverId;
            document.getElementById("selectedDriverDisplay").innerText = name;
        });
    });

    // Initializez harta
    map = L.map('map').setView([45.9432, 24.9668], 7);
    map.attributionControl.setPosition('bottomleft');
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    const orderElements = document.querySelectorAll(".order-data");
    const bounds = L.latLngBounds();

    orderElements.forEach(el => {
        const lat = parseFloat(el.getAttribute("data-latitude"));
        const lng = parseFloat(el.getAttribute("data-longitude"));
        const orderId = el.getAttribute("data-id");
        const address = el.getAttribute("data-address");
        const weight = el.getAttribute("data-weight");
        const volume = el.getAttribute("data-volume");
        const inaccessible = el.getAttribute("data-inaccessible")?.split(',').map(Number) || [];
        const manual = el.getAttribute("data-manual")?.split(',').map(Number) || [];

        if (!isNaN(lat) && !isNaN(lng)) {
            let iconUrl = ICON_URLS.normal;
            const vehicleId = parseInt(vehicleIdInput.value);
            const isRestricted = vehicleId && (inaccessible.includes(vehicleId) || manual.includes(vehicleId));
            if (isRestricted) {
                iconUrl = ICON_URLS.restricted;
            }

            const marker = L.marker([lat, lng], {
                icon: L.icon({
                    iconUrl: iconUrl,
                    iconSize: [25, 41],
                    iconAnchor: [12, 41]
                })
            }).addTo(map);

            marker.orderData = {
                id: orderId,
                address,
                weight,
                volume,
                inaccessible,
                manual,
                selected: false
            };

            marker.bindPopup(() => {
                const currentVehicleId = parseInt(vehicleIdInput.value);
                const isRestricted = currentVehicleId &&
                    (marker.orderData.inaccessible.includes(currentVehicleId) ||
                        marker.orderData.manual.includes(currentVehicleId));
                const isSelected = marker.orderData.selected;

                return `
                    <strong>Order #${marker.orderData.id}</strong><br>
                    ${marker.orderData.address}<br>
                    <small>${marker.orderData.weight} kg, ${marker.orderData.volume} m³</small><br>
                    ${isRestricted
                            ? `<div class="text-danger mt-2">Not available for selected vehicle</div>`
                            : `<button class="btn btn-sm btn-${isSelected ? 'danger' : 'success'} mt-2" onclick="toggleOrderSelection('${marker.orderData.id}')">
                            ${isSelected ? 'Deselect' : 'Select'}
                        </button>`
                        }
                `;
            });

            orderMarkers[orderId] = marker;
            bounds.extend(marker.getLatLng());
        }
    });

    if (Object.keys(orderMarkers).length > 0) {
        map.fitBounds(bounds, { padding: [50, 50] });
    }

    function isOrderRestricted(cb) {
        const vehicleId = parseInt(vehicleIdInput.value);
        const inaccessible = cb.getAttribute("data-inaccessible")?.split(',').map(Number) || [];
        const manual = cb.getAttribute("data-manual")?.split(',').map(Number) || [];
        return inaccessible.includes(vehicleId) || manual.includes(vehicleId);
    }

    function updateCapacityVisuals() {
        let usedWeight = 0, usedVolume = 0;
        const selectedOrders = [];

        Object.values(orderMarkers).forEach(marker => {
            if (marker.orderData.selected) {
                usedWeight += parseFloat(marker.orderData.weight);
                usedVolume += parseFloat(marker.orderData.volume);
                selectedOrders.push(marker.orderData);
            }
        });

        const weightPerc = Math.min((usedWeight / totalWeight) * 100, 100);
        const volumePerc = Math.min((usedVolume / totalVolume) * 100, 100);

        document.getElementById("weightProgress").style.width = weightPerc + "%";
        document.getElementById("volumeProgress").style.width = volumePerc + "%";
        document.getElementById("weightValue").innerText = `${usedWeight.toFixed(1)} / ${totalWeight} kg`;
        document.getElementById("volumeValue").innerText = `${usedVolume.toFixed(1)} / ${totalVolume} m³`;

        document.getElementById("weightProgress").classList.toggle("bg-danger", usedWeight > totalWeight);
        document.getElementById("volumeProgress").classList.toggle("bg-danger", usedVolume > totalVolume);
        createButton.disabled = (usedWeight > totalWeight || usedVolume > totalVolume);

        // Update lista vizuala + inputuri
        selectedOrdersList.innerHTML = "";

        selectedOrders.forEach(order => {
            const container = document.createElement("div");
            container.className = "d-flex justify-content-between align-items-start border rounded p-1 bg-light";
            container.innerHTML = `
            <input type="hidden" name="selectedOrderIds" value="${order.id}" />
            <div class="pe-2">
                <strong>Order #${order.id}</strong><br>
                <small>${order.address}</small><br>
                <small>${order.weight} kg, ${order.volume} m³</small>
            </div>
            <button type="button" class="btn-close ms-2" aria-label="Remove" onclick="toggleOrderSelection('${order.id}')"></button>
        `;
            selectedOrdersList.appendChild(container);
        });

        const placeholder = document.getElementById("selectedOrdersListPlaceholder");
        placeholder.classList.toggle("d-none", selectedOrders.length > 0);
    }

    function updateOrderAvailability() {
        const vehicleId = parseInt(vehicleIdInput.value);
        if (!vehicleId) return;

        Object.values(orderMarkers).forEach(marker => {
            const isRestricted = marker.orderData.inaccessible.includes(vehicleId) || marker.orderData.manual.includes(vehicleId);
            const iconUrl = isRestricted
                ? ICON_URLS.restricted
                : marker.orderData.selected
                    ? ICON_URLS.selected
                    : ICON_URLS.normal;

            marker.setIcon(L.icon({ iconUrl, iconSize: [25, 41], iconAnchor: [12, 41] }));
        });

        updateCapacityVisuals();
    }

    // Initializez toate checkbox-urile
    document.querySelectorAll(".order-checkbox").forEach(cb => {
        cb.addEventListener("change", updateCapacityVisuals);
    });

    // Panoul lateral cu comenzile selectabile
    const panel = document.getElementById("ordersPanel");
    const overlay = document.getElementById("ordersOverlay");

    document.getElementById("toggleOrdersPanel")?.addEventListener("click", () => {
        panel.style.display = "block";
        overlay.style.display = "block";
    });
    document.getElementById("closeOrdersPanel")?.addEventListener("click", () => {
        panel.style.display = "none";
        overlay.style.display = "none";
    });
    overlay?.addEventListener("click", () => {
        panel.style.display = "none";
        overlay.style.display = "none";
    });

    // Functionalitatea de cautare pentru comenzile din panou
    document.getElementById("orderSearch")?.addEventListener("input", e => {
        const term = e.target.value.toLowerCase();
        document.querySelectorAll("#orderListContainer .list-group-item").forEach(item => {
            const match = item.innerText.toLowerCase().includes(term);
            item.style.display = match ? "block" : "none";
        });
    });

    window.toggleOrderSelection = function (orderId) {
        const marker = orderMarkers[orderId];
        const vehicleId = parseInt(document.getElementById("selectedVehicleId").value);

        if (!marker || !vehicleId) return;

        const restricted = marker.orderData.inaccessible.includes(vehicleId) || marker.orderData.manual.includes(vehicleId);
        if (restricted) return;

        marker.orderData.selected = !marker.orderData.selected;

        // Update UI
        const iconUrl = marker.orderData.selected ? ICON_URLS.selected : ICON_URLS.normal;
        marker.setIcon(L.icon({ iconUrl, iconSize: [25, 41], iconAnchor: [12, 41] }));

        updateCapacityVisuals();
        marker.closePopup();
    };

});
