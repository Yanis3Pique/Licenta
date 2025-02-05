document.addEventListener("DOMContentLoaded", function () {
    const stars = document.querySelectorAll(".star");
    const ratingInput = document.getElementById("rating-input");

    stars.forEach(star => {
        star.addEventListener("mouseover", function () {
            let value = parseInt(this.getAttribute("data-value"));
            highlightStars(value);
        });

        star.addEventListener("click", function () {
            let value = parseInt(this.getAttribute("data-value"));
            ratingInput.value = value;
            highlightStars(value);
        });

        star.addEventListener("mouseout", function () {
            let currentRating = parseInt(ratingInput.value) || 0;
            highlightStars(currentRating);
        });
    });

    function highlightStars(value) {
        stars.forEach(star => {
            if (parseInt(star.getAttribute("data-value")) <= value) {
                star.classList.remove("text-secondary");
                star.classList.add("text-warning");
            } else {
                star.classList.remove("text-warning");
                star.classList.add("text-secondary");
            }
        });
    }
});
