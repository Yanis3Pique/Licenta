document.addEventListener("DOMContentLoaded", function () {
    // Iau harta si coordonatele din View-ul Show
    const mapElement = document.getElementById("map");
    const latitude = parseFloat(mapElement.getAttribute("data-lat"));
    const longitude = parseFloat(mapElement.getAttribute("data-lng"));

    // Verific daca-s nr coordonatele
    if (isNaN(latitude) || isNaN(longitude)) {
        console.error("Invalid coordinates.");
        alert("Failed to load the map: Invalid coordinates.");
        return;
    }

    const location = { lat: latitude, lng: longitude };

    // Initalizez harta cu maps
    const map = new google.maps.Map(mapElement, {
        center: location,
        zoom: 16,
        mapTypeId: "roadmap", // Joaca-te cu roadmap, satellite, hybrid, terrain
    });

    // Punem si pointer-ul pe harta
    new google.maps.Marker({
        position: location,
        map: map,
        title: "Location",
    });
});
