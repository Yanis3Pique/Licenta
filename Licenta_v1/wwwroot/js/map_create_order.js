document.addEventListener("DOMContentLoaded", function () {
    // Iau elementele HTML necesare dupa id
    var latitudeInput = document.getElementById('latitude');
    var longitudeInput = document.getElementById('longitude');
    var addressInput = document.getElementById('address');
    var regionSelect = document.getElementById('region');

    // Daca nu am latitudine si longitudine, setez niste valori default cam prin mijlocul Romaniei
    var latitude = parseFloat(latitudeInput.value) || 46.0;
    var longitude = parseFloat(longitudeInput.value) || 25.0;

    // Initializez harta
    var map = L.map('map').setView([latitude, longitude], 13);

    // Adaug un layer de OpenStreetMap
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Adaug un pointer pe harta ca sa vad locatia
    var marker = L.marker([latitude, longitude], { draggable: true }).addTo(map);

    // Updatez inputurile cu latitudinea si longitudinea
    marker.on('dragend', function (e) {
        var latLng = marker.getLatLng();
        latitudeInput.value = latLng.lat;
        longitudeInput.value = latLng.lng;

        // Foloesc API-ul de la OpenStreetMap pentru a gasi adresa
        fetch(`https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${latLng.lat}&lon=${latLng.lng}`)
            .then(response => response.json())
            .then(data => {
                if (data.address) {
                    // Updatez si adresa in input
                    addressInput.value = data.display_name || '';

                    // Incerc sa iau judetul din adresa
                    let county = data.address.county || '';
                    let city = data.address.city || data.address.town || data.address.municipality || '';
                    let sector = '';
                    let addressParts = data.display_name.split(',');

                    // Daca orașul este 'București', iau sectorul
                    for (let i = 0; i < addressParts.length; i++) {
                        if (addressParts[i].toLowerCase() === ' bucharest' && i > 0) {
                            sector = addressParts[i - 1].trim();
                            break;
                        }
                    }

                    // Daca nu am judet, dar am oras 'Bucuresti' si sector, setez judetul ca 'Bucuresti' + sector
                    if (!county && city.toLowerCase() === "bucharest" && sector.toLowerCase().startsWith("sector")) {
                        county = `București ${sector}`;
                    }

                    // Daca nu am nici acum judet, EROARE
                    if (!county) {
                        alert("Could not find a recognized region for this location.");
                        return;
                    }

                    // Setez judetul in dropdown
                    for (let i = 0; i < regionSelect.options.length; i++) {
                        if (regionSelect.options[i].text.trim() === county.trim()) {
                            regionSelect.value = regionSelect.options[i].value;
                            document.getElementById('region-hidden').value = regionSelect.value;
                            break;
                        }
                    }

                    console.log(`County: ${county}`);
                    console.log(`Address: ${addressInput.value}`);
                    console.log(`Latitude: ${latitudeInput.value}`);
                    console.log(`Longitude: ${longitudeInput.value}`);
                    console.log(`Region: ${document.getElementById('region-hidden').value}`)
                }
            })
            .catch(error => {
                console.error("Error:", error);
                alert("Could not fetch address details. Please try again.");
            });
    });
});
