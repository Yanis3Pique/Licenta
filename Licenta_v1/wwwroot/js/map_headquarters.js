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
                if (data.address) {
                    document.getElementById('address').value = data.display_name || '';

                    // Iau judetul, orasul si sectorul din adresa
                    let county = data.address.county || '';
                    let city = data.address.city || data.address.town || data.address.municipality || '';
                    let sector = '';
                    let addressParts = data.display_name.split(',');

                    for (let i = 0; i < addressParts.length; i++) {
                        if (addressParts[i].toLowerCase() === ' bucharest' && i > 0) {
                            sector = addressParts[i - 1].trim();
                            break;
                        }
                    }

                    // Daca nu am judet, dar am oras si sector, atunci e Bucuresti
                    if (!county && city.toLowerCase() === "bucharest" && sector.toLowerCase().startsWith("sector")) {
                        county = `București ${sector}`;
                    }

                    // Daca nu am "judet" nici acum e EROARE
                    if (!county) {
                        alert("Could not find a recognized region for this location.");
                        return;
                    }

                    // Setez judetul in dropdown
                    let regionSelect = document.getElementById('region');
                    for (let i = 0; i < regionSelect.options.length; i++) {
                        if (regionSelect.options[i].text.trim() === county.trim()) {
                            regionSelect.value = regionSelect.options[i].value;
                            break;
                        }
                    }
                }
            })
            .catch(error => {
                console.error("Error:", error);
                alert("Could not fetch address details. Please try again.");
            });
    });
});
