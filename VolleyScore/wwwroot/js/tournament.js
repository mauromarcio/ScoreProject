// File: VolleyScore/wwwroot/js/tournament.js
// Purpose: jQuery UI Sortable for pool team drag-drop reordering;
//          persists order via AJAX on every drop.

(function () {
    'use strict';

    var saveTimeout = null;

    function saveAllPoolOrders() {
        var pools = [];
        $('.sortable-pool').each(function () {
            var poolId = parseInt($(this).data('pool-id'), 10);
            var ttIds = [];
            $(this).find('.pool-team-item').each(function () {
                ttIds.push(parseInt($(this).data('tt-id'), 10));
            });
            pools.push({ poolId: poolId, tournamentTeamIds: ttIds });
        });

        $.ajax({
            url: '/Tournaments/SavePoolOrder',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ pools: pools }),
            success: function (res) {
                if (res && res.success) {
                    // Brief visual feedback
                    var $indicator = $('#saveIndicator');
                    if ($indicator.length) {
                        $indicator.text('Saved').addClass('text-success').removeClass('text-muted');
                        setTimeout(function () {
                            $indicator.text('').removeClass('text-success');
                        }, 1500);
                    }
                }
            },
            error: function () {
                console.warn('Failed to save pool order');
            }
        });
    }

    $(document).ready(function () {
        // Make each pool's team list sortable
        $('.sortable-pool').sortable({
            connectWith: '.sortable-pool',   // allow moving teams between pools
            placeholder: 'pool-team-item ui-sortable-placeholder',
            tolerance: 'pointer',
            cursor: 'grabbing',
            revert: 150,
            stop: function () {
                // Debounce: save 300ms after the last drop
                clearTimeout(saveTimeout);
                saveTimeout = setTimeout(saveAllPoolOrders, 300);
            }
        }).disableSelection();
    });

})();
