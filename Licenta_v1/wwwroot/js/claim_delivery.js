function claim_delivery(deliveryId, button) {
    if (!navigator.geolocation) {
        alert("Geolocation is not supported by your browser.");
        return;
    }

    navigator.geolocation.getCurrentPosition(
        (position) => {
            const latitude = position.coords.latitude;
            const longitude = position.coords.longitude;

            console.log(`GPS Coordinates: Lat=${latitude}, Lon=${longitude}`);

            const form = button.closest("form");

            if (form) {
                let latInput = document.createElement("input");
                latInput.type = "hidden";
                latInput.name = "lat";
                latInput.value = latitude;

                let lonInput = document.createElement("input");
                lonInput.type = "hidden";
                lonInput.name = "lon";
                lonInput.value = longitude;

                form.appendChild(latInput);
                form.appendChild(lonInput);

                form.submit();
            }
        },
        (error) => {
            console.error("Geolocation error:", error);
            alert("Geolocation failed! Please enable location services and try again.");
        }
    );
}
