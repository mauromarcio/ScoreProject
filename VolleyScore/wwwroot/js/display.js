// File: VolleyScore/wwwroot/js/display.js
// Purpose: Part 2 – Big-screen display logic.
//   Connects to SignalR hub, listens for ScoreUpdated events from the operator,
//   and animates the score numbers in real time without any page reload.

(function () {
    'use strict';

    var matchId    = DISPLAY_DATA.matchId;
    var totalSets  = DISPLAY_DATA.totalSets;

    // ── DOM element cache ─────────────────────────────────────────────────────
    var $homeScore    = $('#displayHomeScore');
    var $awayScore    = $('#displayAwayScore');
    var $homeSets     = $('#displayHomeSets');
    var $awaySets     = $('#displayAwaySets');
    var $homeDots     = $('#displayHomeDots');
    var $awayDots     = $('#displayAwayDots');
    var $setNumber    = $('#displaySetNumber');
    var $homeServing  = $('#displayHomeServing');
    var $awayServing  = $('#displayAwayServing');
    var $connDot      = $('#connDot');
    var $connText     = $('#connText');

    // ── Apply update from SignalR ──────────────────────────────────────────────
    function applyUpdate(result) {
        // Animate score change
        animateScore($homeScore, result.homeScore);
        animateScore($awayScore, result.awayScore);

        // Sets won
        $homeSets.text(result.homeSetsWon);
        $awaySets.text(result.awaySetsWon);

        // Set number
        $setNumber.text(result.currentSetNumber);

        // Update set dots
        updateDots($homeDots, result.homeSetsWon);
        updateDots($awayDots, result.awaySetsWon);

        // Serving indicator
        if (result.homeIsServing) {
            $homeServing.removeClass('hidden');
            $awayServing.addClass('hidden');
        } else {
            $homeServing.addClass('hidden');
            $awayServing.removeClass('hidden');
        }

        // Match complete – show winner
        if (result.matchCompleted && result.winnerName) {
            showMatchComplete(result.winnerName);
        }
    }

    // ── Score flash animation ─────────────────────────────────────────────────
    function animateScore($el, newValue) {
        var current = parseInt($el.text()) || 0;
        if (current === newValue) return;

        $el.addClass('score-flash');
        $el.text(newValue);

        setTimeout(function () { $el.removeClass('score-flash'); }, 350);
    }

    // ── Set dots update ───────────────────────────────────────────────────────
    function updateDots($container, setsWon) {
        $container.find('.display-set-dot').each(function (i) {
            if (i < setsWon) $(this).addClass('won');
            else $(this).removeClass('won');
        });
    }

    // ── Match complete overlay ────────────────────────────────────────────────
    function showMatchComplete(winnerName) {
        // Create and inject a simple overlay
        if ($('#matchWinOverlay').length) return;
        var overlay = $('<div>')
            .attr('id', 'matchWinOverlay')
            .css({
                position: 'fixed',
                inset: 0,
                background: 'rgba(0,0,0,0.85)',
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                zIndex: 9999
            })
            .html(
                '<div style="text-align:center;animation:none">' +
                '<div style="font-size:5rem;">🏆</div>' +
                '<div style="font-size:clamp(2.5rem,6vw,5rem);font-weight:900;color:#f5c542;margin-top:16px">' +
                htmlEscape(winnerName) + '</div>' +
                '<div style="font-size:2rem;color:#aaa;margin-top:8px">WINS THE MATCH</div>' +
                '</div>'
            );
        $('body').append(overlay);
    }

    function htmlEscape(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // ── SignalR connection ─────────────────────────────────────────────────────
    function initSignalR() {
        var connection = new signalR.HubConnectionBuilder()
            .withUrl('/scoreHub')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000])
            .build();

        // Receive score updates
        connection.on('ScoreUpdated', function (result) {
            applyUpdate(result);
        });

        // Connection lifecycle
        connection.onreconnecting(function () {
            $connDot.removeClass('connected').addClass('disconnected');
            $connText.text('Reconnecting…');
        });

        connection.onreconnected(function () {
            $connDot.removeClass('disconnected').addClass('connected');
            $connText.text('');
            connection.invoke('JoinMatchGroup', matchId).catch(console.error);
        });

        connection.onclose(function () {
            $connDot.removeClass('connected').addClass('disconnected');
            $connText.text('Disconnected');
        });

        // Start and join the match group
        connection.start()
            .then(function () {
                $connDot.addClass('connected');
                return connection.invoke('JoinMatchGroup', matchId);
            })
            .catch(function (err) {
                $connDot.addClass('disconnected');
                $connText.text('Connection failed');
                console.error('SignalR error:', err.toString());

                // Fallback: poll every 5 seconds if SignalR fails
                startPollingFallback();
            });
    }

    // ── Polling fallback (if SignalR unavailable) ──────────────────────────────
    var pollingInterval = null;

    function startPollingFallback() {
        if (pollingInterval) return;
        $connText.text('Polling…');
        pollingInterval = setInterval(function () {
            $.getJSON('/Score/GetCurrentScore?matchId=' + matchId)
                .done(function (result) {
                    if (result.success) applyUpdate(result);
                })
                .fail(function () { /* silent */ });
        }, 3000);
    }

    // ── Init ──────────────────────────────────────────────────────────────────
    $(document).ready(function () {
        // Apply initial serving state from server-rendered data
        if (DISPLAY_DATA.homeScore !== undefined) {
            // Already rendered by server; just set up live updates
        }

        initSignalR();

        // Keep screen awake (prevent display sleep) using Wake Lock API
        if ('wakeLock' in navigator) {
            navigator.wakeLock.request('screen').catch(function () {});
        }
    });

})();
