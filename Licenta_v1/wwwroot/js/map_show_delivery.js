document.addEventListener("DOMContentLoaded", function () {
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

    var validOrders = ordersData.filter(o => !isNaN(o.latitude) && !isNaN(o.longitude));
    if (validOrders.length === 0) {
        console.error("No valid orders with location data.");
        return;
    }

    // Nu dau un centru fix hartii, o las sa se ajusteze
    var map = L.map('map', { zoomControl: true });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Creez o limita pentru a afisa toate locatiile
    var bounds = L.latLngBounds();

    validOrders.forEach(function (order) {
        var marker = L.marker([order.latitude, order.longitude]).addTo(map)
            .bindPopup(`<b>Order ${order.id}</b><br>${order.address}`);
        bounds.extend(marker.getLatLng()); // Cresc limita ca sa includ si locatia asta
    });

    // Reglez zoom-ul pentru a afisa toate locatiile
    if (validOrders.length > 1) {
        map.fitBounds(bounds, { padding: [50, 50] });
    } else {
        // Daca e o singura Order, o pun in centru
        map.setView([validOrders[0].latitude, validOrders[0].longitude], 14);
    }
});
