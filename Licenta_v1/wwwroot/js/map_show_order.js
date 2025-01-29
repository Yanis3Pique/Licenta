document.addEventListener("DOMContentLoaded", function () {
    var latitude = parseFloat(document.getElementById("orderLatitude").value);
    var longitude = parseFloat(document.getElementById("orderLongitude").value);
    var address = document.getElementById("orderAddress").value;

    if (!latitude || !longitude) {
        console.error("No valid latitude or longitude found.");
        return;
    }

    var map = L.map('map').setView([latitude, longitude], 14);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    L.marker([latitude, longitude]).addTo(map)
        .bindPopup("<b>Order Location</b><br>" + address)
        .openPopup();
});