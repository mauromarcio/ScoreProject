// File: VolleyScore/wwwroot/js/site.js
// Purpose: Global site-level JavaScript (Bootstrap tooltip init, etc.)

(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        // Initialise Bootstrap tooltips
        var tooltipEls = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
        tooltipEls.forEach(function (el) {
            new bootstrap.Tooltip(el);
        });

        // Auto-dismiss alerts after 4 seconds
        var alerts = document.querySelectorAll('.alert.alert-success, .alert.alert-danger');
        alerts.forEach(function (alert) {
            setTimeout(function () {
                var bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
                if (bsAlert) bsAlert.close();
            }, 4000);
        });
    });

})();
