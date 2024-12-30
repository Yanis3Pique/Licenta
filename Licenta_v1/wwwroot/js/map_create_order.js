document.addEventListener("DOMContentLoaded", function () {
    const defaultLat = 46.0;
    const defaultLng = 25.0;

    // Iau coordonatele si regiunea in variabile
    var latitudeInput = document.getElementById('latitude');
    var longitudeInput = document.getElementById('longitude');
    var regionSelect = document.getElementById('region');
    var hiddenRegionInput = document.getElementById('hiddenRegion'); // Input hidden pentru regiune

    var latitude = parseFloat(latitudeInput.value) || defaultLat;
    var longitude = parseFloat(longitudeInput.value) || defaultLng;

    // Initializez harta cu un pointer default in Romania
    var map = L.map('map').setView([latitude, longitude], 13);

    // Adaug un layer OpenStreetMap
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '© OpenStreetMap contributors'
    }).addTo(map);

    // Pointer pt o adresa default
    var marker = L.marker([latitude, longitude], { draggable: true }).addTo(map);

    // Actualizez regiunea in input-ul ascuns
    function updateHiddenRegion() {
        hiddenRegionInput.value = regionSelect.value;
    }

    // Setez initial regiunea ascunsa
    updateHiddenRegion();

    // Updatez coordonatele in input-uri și regiunea
    marker.on('dragend', function (e) {
        const latLng = marker.getLatLng();
        latitudeInput.value = latLng.lat;
        longitudeInput.value = latLng.lng;

        // Fac reverse geocoding pentru a obtine adresa in text
        fetch(`https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${latLng.lat}&lon=${latLng.lng}`)
            .then(response => response.json())
            .then(data => {
                if (data.address) {
                    document.getElementById('address').value = data.display_name || '';

                    let county = data.address.county || '';
                    let city = data.address.city || data.address.town || data.address.municipality || '';
                    let sector = '';

                    if (city.toLowerCase() === "bucharest") {
                        let parts = data.display_name.split(',');
                        for (let i = 0; i < parts.length; i++) {
                            if (parts[i].toLowerCase().includes("sector")) {
                                sector = parts[i].trim();
                                county = `București ${sector}`;
                                break;
                            }
                        }
                    }

                    console.log("Regiune valida:", county);

                    // Setez judetul in functie de adresa
                    let found = false;
                    for (let i = 0; i < regionSelect.options.length; i++) {
                        console.log(regionSelect.options[i].text.trim(), county.trim());
                        if (regionSelect.options[i].text.trim() === county.trim()) {
                            regionSelect.value = regionSelect.options[i].value;
                            found = true;
                            updateHiddenRegion(); // Actualizez regiunea ascunsa
                            break;
                        }
                    }

                    if (!found) {
                        console.log("Could not match the address with a known region.");
                        alert("Could not match the address with a known region.");
                    }
                }
            })
            .catch(error => {
                console.error("Error fetching address details:", error);
                alert("Could not fetch address details. Please try again.");
            });
    });
});
