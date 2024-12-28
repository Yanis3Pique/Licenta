document.addEventListener("DOMContentLoaded", function () {
    const latitude = parseFloat("@Model.Latitude");
    const longitude = parseFloat("@Model.Longitude");
    const location = { lat: latitude, lng: longitude };

    // Creez harta la locatia specificata
    const map = new google.maps.Map(document.getElementById("map"), {
        center: location,
        zoom: 16,
    });

    // Pun un marker pe locatie
    new google.maps.Marker({
        position: location,
        map: map,
        title: "@Model.Name",
    });

    // Creez un panorama la locatie specificata
    const streetViewPanorama = new google.maps.StreetViewPanorama(
        document.getElementById("map"),
        {
            position: location,
            pov: {
                heading: 34,
                pitch: 10,
            },
            zoom: 1,
        }
    );

    // Pun panorama pe harta
    map.setStreetView(streetViewPanorama);
});