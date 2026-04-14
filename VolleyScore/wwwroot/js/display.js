// File: VolleyScore/wwwroot/js/display.js
// Purpose: Part 2 – Big-screen display logic.
//   Connects to SignalR hub, listens for ScoreUpdated events from the operator,
//   and animates the score numbers in real time without any page reload.
//   Team sides are INVERTED vs court: DISPLAY_DATA.homeTeamOnLeft already carries
//   the negated value so the mapping below is identical to the original logic.

(function () {
    'use strict';

    var matchId        = DISPLAY_DATA.matchId;
    var totalSets      = DISPLAY_DATA.totalSets;
    // homeTeamOnLeft here means "home is on the LEFT of this display" (already inverted from court)
    var homeTeamOnLeft = DISPLAY_DATA.homeTeamOnLeft;

    // ── DOM element cache ─────────────────────────────────────────────────────
    var $leftScore    = $('#displayLeftScore');
    var $rightScore   = $('#displayRightScore');
    var $leftSets     = $('#displayLeftSets');
    var $rightSets    = $('#displayRightSets');
    var $leftDots     = $('#displayLeftDots');
    var $rightDots    = $('#displayRightDots');
    var $setNumber    = $('#displaySetNumber');
    var $leftServing  = $('#displayLeftServing');
    var $rightServing = $('#displayRightServing');
    var $leftName     = $('#displayLeftTeamName');
    var $rightName    = $('#displayRightTeamName');
    var $connDot      = $('#connDot');
    var $connText     = $('#connText');

    // ── Apply update from SignalR ──────────────────────────────────────────────
    function applyUpdate(result) {
        // Invert the court-side flag to get the display-side flag
        var htol = (result.homeTeamOnLeft !== undefined) ? !result.homeTeamOnLeft : homeTeamOnLeft;

        // If display-side assignment changed, swap team name labels
        if (htol !== homeTeamOnLeft) {
            homeTeamOnLeft = htol;
            var leftName  = htol ? DISPLAY_DATA.homeTeamName : DISPLAY_DATA.awayTeamName;
            var rightName = htol ? DISPLAY_DATA.awayTeamName : DISPLAY_DATA.homeTeamName;
            $leftName.text(leftName);
            $rightName.text(rightName);
        }

        // Map home/away data to left/right based on display-side assignment
        var leftScore     = htol ? result.homeScore    : result.awayScore;
        var rightScore    = htol ? result.awayScore    : result.homeScore;
        var leftSets      = htol ? result.homeSetsWon  : result.awaySetsWon;
        var rightSets     = htol ? result.awaySetsWon  : result.homeSetsWon;
        var leftIsServing = htol ? result.homeIsServing : !result.homeIsServing;

        // Animate score change
        animateScore($leftScore, leftScore);
        animateScore($rightScore, rightScore);

        // Sets won counters
        $leftSets.text(leftSets);
        $rightSets.text(rightSets);

        // Set number
        $setNumber.text(result.currentSetNumber);

        // Update set dots
        updateDots($leftDots, leftSets);
        updateDots($rightDots, rightSets);

        // Serving indicator
        if (leftIsServing) {
            $leftServing.removeClass('hidden');
            $rightServing.addClass('hidden');
        } else {
            $leftServing.addClass('hidden');
            $rightServing.removeClass('hidden');
        }

        // Match complete – show winner overlay
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
            else             $(this).removeClass('won');
        });
    }

    // ── Match complete overlay ────────────────────────────────────────────────
    function showMatchComplete(winnerName) {
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
                '<div style="text-align:center">' +
                '<div style="font-size:5rem;">&#127942;</div>' +
                '<div style="font-size:clamp(2.5rem,6vw,5rem);font-weight:900;color:#ffe033;margin-top:16px">' +
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

    // ── Font size controls ────────────────────────────────────────────────────
    var SCORE_SIZE_KEY     = 'vsDisplayFontSz';
    var SCORE_SIZE_MIN     = 10;   // vw
    var SCORE_SIZE_MAX     = 36;   // vw
    var SCORE_SIZE_DEFAULT = 24;   // vw
    var SCORE_SIZE_STEP    = 2;    // vw
    var HIDE_DELAY         = 10000; // ms
    var hideTimer          = null;

    function getScoreSize() {
        var v = parseInt(localStorage.getItem(SCORE_SIZE_KEY));
        return (isNaN(v) || v < SCORE_SIZE_MIN || v > SCORE_SIZE_MAX) ? SCORE_SIZE_DEFAULT : v;
    }

    function applyScoreSize(vw) {
        document.documentElement.style.setProperty('--score-font-size', vw + 'vw');
    }

    function showSizeControls() {
        $('#sizeControls').addClass('visible');
        resetHideTimer();
    }

    function resetHideTimer() {
        if (hideTimer) clearTimeout(hideTimer);
        hideTimer = setTimeout(function () {
            $('#sizeControls').removeClass('visible');
        }, HIDE_DELAY);
    }

    function initSizeControls() {
        // Apply stored (or default) size immediately
        applyScoreSize(getScoreSize());

        // Show controls on click or touch anywhere on the screen
        $(document).on('click', showSizeControls);
        document.addEventListener('touchstart', showSizeControls, { passive: true });

        // − button
        $('#sizeMinus').on('click', function (e) {
            e.stopPropagation();
            var next = Math.max(SCORE_SIZE_MIN, getScoreSize() - SCORE_SIZE_STEP);
            localStorage.setItem(SCORE_SIZE_KEY, next);
            applyScoreSize(next);
            resetHideTimer();
        });

        // + button
        $('#sizePlus').on('click', function (e) {
            e.stopPropagation();
            var next = Math.min(SCORE_SIZE_MAX, getScoreSize() + SCORE_SIZE_STEP);
            localStorage.setItem(SCORE_SIZE_KEY, next);
            applyScoreSize(next);
            resetHideTimer();
        });
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
            $connText.text('Reconnecting\u2026');
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
        $connText.text('Polling\u2026');
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
        initSignalR();
        initSizeControls();

        // Keep screen awake (prevent display sleep) using Wake Lock API
        if ('wakeLock' in navigator) {
            navigator.wakeLock.request('screen').catch(function () {});
        }
    });

})();
