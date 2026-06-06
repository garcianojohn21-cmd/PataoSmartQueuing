document.addEventListener("DOMContentLoaded", function () {
    const sidebarToggle = document.getElementById("sidebarToggle");
    const wrapper = document.getElementById("wrapper");

    // Toggle sidebar
    sidebarToggle.addEventListener("click", function () {
        wrapper.classList.toggle("toggled");
    });

    // Highlight active link
    const links = document.querySelectorAll("#sidebar-wrapper .list-group-item");
    links.forEach(link => {
        if (link.href === window.location.href) {
            link.classList.add("active");
        }

        link.addEventListener("click", function () {
            links.forEach(l => l.classList.remove("active"));
            this.classList.add("active");
        });
    });
});
