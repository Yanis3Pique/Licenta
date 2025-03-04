document.addEventListener("DOMContentLoaded", function () {
    // Iau containerul de progres si capacitatile totale din atributele de date
    var progressContainer = document.getElementById("capacity-progress");
    if (!progressContainer) return;

    var totalWeight = parseFloat(progressContainer.getAttribute("data-total-weight")) || 1;
    var totalVolume = parseFloat(progressContainer.getAttribute("data-total-volume")) || 1;

    // Iau butonul de update delivery
    var updateButton = document.getElementById("updateDeliveryButton");

    // Functie pentru actualizarea vizualizarii capacitatii pe baza comenzilor selectate
    function updateCapacityVisuals() {
        // Selectez toate casetele de selectare care au atribute de date de greutate si volum
        var orderCheckboxes = document.querySelectorAll("input.form-check-input[data-weight][data-volume]");
        var usedWeight = 0, usedVolume = 0;

        orderCheckboxes.forEach(function (checkbox) {
            if (checkbox.checked) {
                var weight = parseFloat(checkbox.getAttribute("data-weight")) || 0;
                var volume = parseFloat(checkbox.getAttribute("data-volume")) || 0;
                usedWeight += weight;
                usedVolume += volume;
            }
        });

        // Calculez procentajul de utilizare; daca depaseste 100, afisez 100%
        var weightPercentage = (usedWeight / totalWeight) * 100;
        var volumePercentage = (usedVolume / totalVolume) * 100;
        var weightDisplay = Math.min(weightPercentage, 100);
        var volumeDisplay = Math.min(volumePercentage, 100);

        var weightProgress = document.getElementById("weightProgress");
        var volumeProgress = document.getElementById("volumeProgress");
        var weightValue = document.getElementById("weightValue");
        var volumeValue = document.getElementById("volumeValue");

        // Actualizez progress bar-ul pentru greutate
        if (weightProgress) {
            weightProgress.style.width = weightDisplay + "%";
            weightProgress.setAttribute("aria-valuenow", usedWeight);
            if (usedWeight > totalWeight) {
                weightProgress.classList.remove("bg-primary");
                weightProgress.classList.add("bg-danger");
            } else {
                weightProgress.classList.remove("bg-danger");
                weightProgress.classList.add("bg-primary");
            }
        }

        // Actualizez progress bar-ul pentru volum
        if (volumeProgress) {
            volumeProgress.style.width = volumeDisplay + "%";
            volumeProgress.setAttribute("aria-valuenow", usedVolume);
            if (usedVolume > totalVolume) {
                volumeProgress.classList.remove("bg-info");
                volumeProgress.classList.add("bg-danger");
            } else {
                volumeProgress.classList.remove("bg-danger");
                volumeProgress.classList.add("bg-info");
            }
        }

        // Actualizez valorile afisate sub progress bars si afisez avertisment daca depaseste capacitatea
        if (weightValue) {
            if (usedWeight > totalWeight) {
                weightValue.innerText = usedWeight.toFixed(1) + " / " + totalWeight + " kg - Exceeds capacity!";
                weightValue.style.color = "red";
            } else {
                weightValue.innerText = usedWeight.toFixed(1) + " / " + totalWeight + " kg";
                weightValue.style.color = "";
            }
        }
        if (volumeValue) {
            if (usedVolume > totalVolume) {
                volumeValue.innerText = usedVolume.toFixed(1) + " / " + totalVolume + " m³ - Exceeds capacity!";
                volumeValue.style.color = "red";
            } else {
                volumeValue.innerText = usedVolume.toFixed(1) + " / " + totalVolume + " m³";
                volumeValue.style.color = "";
            }
        }

        // Dezactivez butonul daca capacitatea este depasita
        if (updateButton) {
            if (usedWeight > totalWeight || usedVolume > totalVolume) {
                updateButton.disabled = true;
            } else {
                updateButton.disabled = false;
            }
        }
    }

    // Atasez evenimentul "change" la toate casetele de selectare
    var orderCheckboxes = document.querySelectorAll("input.form-check-input[data-weight][data-volume]");
    orderCheckboxes.forEach(function (checkbox) {
        checkbox.addEventListener("change", updateCapacityVisuals);
    });

    updateCapacityVisuals();
});
