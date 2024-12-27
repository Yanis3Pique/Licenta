document.addEventListener("DOMContentLoaded", function () {
    // Preiau coordonatele deja existente, sau daca nu -> default
    var latitude = parseFloat(document.getElementById('latitude').value) || 46.0;
    var longitude = parseFloat(document.getElementById('longitude').value) || 25.0;

    // Initializez harta cu un pointer default in Romania
    var map = L.map('map').setView([latitude, longitude], 13);

    // Adaug un layer OpenStreetMap
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Pointer pt o adresa default
    var marker = L.marker([latitude, longitude], { draggable: true }).addTo(map);

    // Updatez coordonatele in input-uri
    marker.on('dragend', function (e) {
        var latLng = marker.getLatLng();
        document.getElementById('latitude').value = latLng.lat;
        document.getElementById('longitude').value = latLng.lng;

        // Fac reverse geocoding pentru a obtine adresa in text
        fetch(`https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${latLng.lat}&lon=${latLng.lng}`)
            .then(response => response.json())
            .then(data => {
                if (data.display_name) {
                    document.getElementById('homeAddress').value = data.display_name;
                }
            });
    });
});
