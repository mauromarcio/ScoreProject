// File: VolleyScore/wwwroot/js/court.js
// Purpose: Handles all court-view interactions:
//   - jQuery UI drag-and-drop for player positioning
//   - Score button AJAX calls
//   - SignalR subscription for round-trip confirmation
//   - Court position updates after rotation
//   - Side-switching, match start, overlay display

(function () {
    'use strict';

    // ── State ─────────────────────────────────────────────────────────────────
    var matchId     = COURT_DATA.matchId;
    var setNumber   = COURT_DATA.setNumber;
    var matchStatus = COURT_DATA.matchStatus;   // 'Setup' | 'InProgress' | 'Completed'
    var homeTeamOnLeft = COURT_DATA.homeTeamOnLeft;
    var homeScore   = COURT_DATA.homeScore;
    var awayScore   = COURT_DATA.awayScore;
    var homeIsServing = COURT_DATA.homeIsServing;
    var homeSetsWon = COURT_DATA.homeSetsWon;
    var awaySetsWon = COURT_DATA.awaySetsWon;
    var totalSets   = COURT_DATA.totalSets;

    var isBusy = false; // prevents double-clicks on score buttons

    // ── jQuery UI Drag & Drop ─────────────────────────────────────────────────
    function initDragDrop() {
        // Make each player badge draggable
        $('.player-badge').draggable({
            revert: 'invalid',
            zIndex: 9999,
            cursor: 'grabbing',
            opacity: 0.85,
            helper: function () {
                // Clone the badge as the drag visual
                return $(this).clone()
                    .css({ width: $(this).outerWidth(), 'pointer-events': 'none' });
            },
            start: function (event, ui) {
                $(this).css('opacity', 0.4);
            },
            stop: function (event, ui) {
                $(this).css('opacity', '');
            }
        });

        // Make each position slot droppable
        $('.position-slot').droppable({
            accept: function (draggable) {
                // Only accept badges that belong to the same side
                var dragSide  = draggable.data('side');
                var dropSide  = $(this).data('side');
                return dragSide === dropSide;
            },
            hoverClass: 'ui-droppable-hover',
            drop: function (event, ui) {
                var slot      = $(this);
                var badge     = ui.draggable;
                var playerId  = badge.data('player-id');
                var playerNum = badge.data('player-number');
                var playerName = badge.data('player-name');
                var side      = badge.data('side');
                var position  = parseInt(slot.data('position'));

                savePosition(playerId, playerNum, playerName, side, position, slot, badge);
            }
        });

        // Double-clicking a slot removes the player (returns to roster)
        $(document).on('dblclick', '.position-slot.occupied', function () {
            var slot = $(this);
            var side = slot.data('side');
            // Find which player is in this slot by reading the current number
            var numEl = slot.find('.slot-number');
            var playerNum = parseInt(numEl.text());

            // Find the badge with this number on this side
            var badge = $('.player-badge[data-side="' + side + '"][data-player-number="' + playerNum + '"]');
            if (badge.length) {
                var playerId = badge.data('player-id');
                removeFromSlot(playerId, side, slot, badge);
            }
        });
    }

    // ── Save a player position via AJAX ───────────────────────────────────────
    function savePosition(playerId, playerNum, playerName, side, position, slot, badge) {
        $.ajax({
            url: '/Matches/SavePosition',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                matchId:   matchId,
                setNumber: setNumber,
                playerId:  playerId,
                position:  position,
                side:      side
            }),
            success: function (result) {
                if (!result.success) {
                    showError(result.error || 'Could not save position.');
                    return;
                }

                // If this position had a previous occupant, free that badge
                if (slot.hasClass('occupied')) {
                    var oldNumEl = slot.find('.slot-number');
                    var oldNum = parseInt(oldNumEl.text());
                    if (oldNum !== playerNum) {
                        var oldBadge = $('.player-badge[data-side="' + side + '"][data-player-number="' + oldNum + '"]');
                        oldBadge.removeClass('on-court');
                    }
                }

                // Update the slot visually
                slot.addClass('occupied');
                slot.html('<span class="slot-number">' + playerNum + '</span>' +
                          '<span class="slot-player-name">' + playerName + '</span>');

                // Mark the badge as on-court
                badge.addClass('on-court');

                // If the badge came from somewhere else on-court, clear that slot
                // (handled server-side; re-check other slots for this player)
                $('.position-slot[data-side="' + side + '"]').each(function () {
                    var s = $(this);
                    if (s.data('position') != position && s.hasClass('occupied')) {
                        var num = parseInt(s.find('.slot-number').text());
                        if (num === playerNum) {
                            clearSlot(s, position);
                        }
                    }
                });
            },
            error: function () {
                showError('Network error saving position. Please try again.');
            }
        });
    }

    // ── Remove player from slot ───────────────────────────────────────────────
    function removeFromSlot(playerId, side, slot, badge) {
        var position = parseInt(slot.data('position'));

        $.ajax({
            url: '/Matches/SavePosition',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                matchId:   matchId,
                setNumber: setNumber,
                playerId:  playerId,
                position:  0,   // 0 = remove
                side:      side
            }),
            success: function (result) {
                if (!result.success) { showError(result.error); return; }
                clearSlot(slot, position);
                badge.removeClass('on-court');
            },
            error: function () { showError('Could not remove player.'); }
        });
    }

    function clearSlot(slot, position) {
        slot.removeClass('occupied');
        slot.html('<span class="slot-number" style="color:#bbb">' + position + '</span>');
    }

    // ── Score Buttons ──────────────────────────────────────────────────────────
    // These functions are called from onclick= in the view (global scope needed)
    window.addPoint = function (side) {
        if (isBusy || matchStatus === 'Completed') return;
        isBusy = true;
        postScore('/Score/AddPoint', side, function () { isBusy = false; });
    };

    window.subtractPoint = function (side) {
        if (isBusy || matchStatus === 'Completed') return;
        isBusy = true;
        postScore('/Score/SubtractPoint', side, function () { isBusy = false; });
    };

    function postScore(url, side, done) {
        $.ajax({
            url: url,
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ matchId: matchId, team: side }),
            success: function (result) {
                if (!result.success) {
                    showError(result.error || 'Score update failed.');
                    done();
                    return;
                }
                applyScoreUpdate(result);
                done();
            },
            error: function (xhr) {
                var msg = 'Error updating score.';
                try { msg = JSON.parse(xhr.responseText).error || msg; } catch (e) { }
                showError(msg);
                done();
            }
        });
    }

    // ── Apply a score result from the server ──────────────────────────────────
    function applyScoreUpdate(result) {
        // Determine which side maps to home/away based on current homeTeamOnLeft
        var leftScore  = homeTeamOnLeft ? result.homeScore  : result.awayScore;
        var rightScore = homeTeamOnLeft ? result.awayScore  : result.homeScore;
        var leftSets   = homeTeamOnLeft ? result.homeSetsWon  : result.awaySetsWon;
        var rightSets  = homeTeamOnLeft ? result.awaySetsWon  : result.homeSetsWon;
        var leftIsServing  = homeTeamOnLeft ? result.homeIsServing : !result.homeIsServing;

        // Update scores with flip animation
        updateScoreDisplay('left', leftScore);
        updateScoreDisplay('right', rightScore);

        // Update set number
        setNumber = result.currentSetNumber;
        $('#setNumberDisplay').text(result.currentSetNumber);

        // Update serving badges
        updateServingBadge(leftIsServing);

        // Update set dots
        updateSetDots('left', leftSets);
        updateSetDots('right', rightSets);

        // Update court positions if rotation occurred
        if (result.homeCourtPositions && result.awayCourtPositions) {
            var leftPositions  = homeTeamOnLeft ? result.homeCourtPositions : result.awayCourtPositions;
            var rightPositions = homeTeamOnLeft ? result.awayCourtPositions : result.homeCourtPositions;
            renderCourtPositions('left', leftPositions, leftIsServing);
            renderCourtPositions('right', rightPositions, !leftIsServing);
        }

        // Cache current state
        homeScore   = result.homeScore;
        awayScore   = result.awayScore;
        homeIsServing = result.homeIsServing;
        homeSetsWon = result.homeSetsWon;
        awaySetsWon = result.awaySetsWon;

        // ── Update tally panel ────────────────────────────────────────────────
        updateTallyScores(leftScore, rightScore);

        // Update next server from rotated positions
        var homePositions = result.homeCourtPositions || [];
        var awayPositions = result.awayCourtPositions || [];
        var leftPositions  = homeTeamOnLeft ? homePositions : awayPositions;
        var rightPositions = homeTeamOnLeft ? awayPositions : homePositions;
        var leftServer  = getServerFromPositions(leftPositions)  || getServerFromDom('left');
        var rightServer = getServerFromPositions(rightPositions) || getServerFromDom('right');
        updateTallyServer(leftServer, rightServer, leftIsServing);

        // Handle set/match completion
        if (result.matchCompleted) {
            matchStatus = 'Completed';
            $('#btnStartMatch').hide();
            $('#matchStatusText').text('Completed').removeClass('bg-secondary bg-success').addClass('bg-dark');
            $('#matchCompleteOverlay').show();
            $('#winnerText').text(result.winnerName + ' Wins!');
            $('#winnerSubText').text('Match complete – ' +
                result.homeSetsWon + ':' + result.awaySetsWon + ' sets');
        } else if (result.setCompleted) {
            showSetComplete(result);
        }
    }

    // ── Score digit flip animation ─────────────────────────────────────────────
    function updateScoreDisplay(side, newScore) {
        var str = String(newScore).padStart(2, '0');
        var d0 = $('#' + side + 'Digit0');
        var d1 = $('#' + side + 'Digit1');

        if (d0.text() !== str[0]) {
            d0.text(str[0]);
            d0.addClass('flip');
            setTimeout(function () { d0.removeClass('flip'); }, 400);
        }
        if (d1.text() !== str[1]) {
            d1.text(str[1]);
            d1.addClass('flip');
            setTimeout(function () { d1.removeClass('flip'); }, 400);
        }
    }

    // ── Serving badge update ──────────────────────────────────────────────────
    function updateServingBadge(leftIsServing) {
        var lb = $('#leftServingBadge');
        var rb = $('#rightServingBadge');
        if (leftIsServing) {
            lb.css('visibility', 'visible');
            rb.css('visibility', 'hidden');
        } else {
            lb.css('visibility', 'hidden');
            rb.css('visibility', 'visible');
        }
        // Highlight position 1 on the serving side
        $('.position-slot').removeClass('serving-team');
        var servingSideHalf = leftIsServing ? '#leftHalf' : '#rightHalf';
        $(servingSideHalf + ' .position-slot[data-position="1"]').addClass('serving-team');
    }

    // ── Set dots update ───────────────────────────────────────────────────────
    function updateSetDots(side, setsWon) {
        var container = side === 'left' ? $('#leftSetsDisplay') : $('#rightSetsDisplay');
        container.find('.set-dot').each(function (i) {
            if (i < setsWon) $(this).addClass('won');
            else $(this).removeClass('won');
        });
    }

    // ── Render court positions after rotation ─────────────────────────────────
    function renderCourtPositions(side, positions, isServing) {
        var halfId = side === 'left' ? '#leftHalf' : '#rightHalf';
        var sideStr = $(halfId).data('side');

        positions.forEach(function (pos) {
            var slot = $(halfId + ' .position-slot[data-position="' + pos.position + '"]');
            if (!slot.length) return;

            slot.removeClass('serving-team');
            if (pos.position === 1 && isServing) slot.addClass('serving-team');

            if (pos.player) {
                slot.addClass('occupied');
                slot.html('<span class="slot-number">' + pos.player.number + '</span>' +
                          '<span class="slot-player-name">' + pos.player.name + '</span>');

                // Update badge state
                var badge = $('#badge-' + pos.player.id);
                badge.addClass('on-court');
            } else {
                var posNum = pos.position;
                slot.removeClass('occupied');
                slot.html('<span class="slot-number" style="color:#bbb">' + posNum + '</span>');
            }
        });

        // Refresh badge on-court state for this side
        var rosterListId = side === 'left' ? '#leftRosterList' : '#rightRosterList';
        $(rosterListId + ' .player-badge').each(function () {
            var pid = $(this).data('player-id');
            var isOnCourt = positions.some(function (p) { return p.player && p.player.id === pid; });
            $(this).toggleClass('on-court', isOnCourt);
        });
    }

    // ── Set complete overlay ──────────────────────────────────────────────────
    function showSetComplete(result) {
        var hs = homeTeamOnLeft ? result.homeSetsWon : result.awaySetsWon;
        var as = homeTeamOnLeft ? result.awaySetsWon : result.homeSetsWon;
        $('#setWinnerText').text('Set ' + (result.currentSetNumber - 1) + ' Complete!');
        $('#setCompleteSubText').text('Sets: ' + hs + ' – ' + as +
            ' | Starting Set ' + result.currentSetNumber);
        $('#setCompleteOverlay').show();
    }

    window.dismissSetComplete = function () {
        $('#setCompleteOverlay').hide();
        // Reload to re-initialise drag-drop for the new set
        window.location.reload();
    };

    // ── Switch sides ──────────────────────────────────────────────────────────
    window.switchSides = function () {
        $.ajax({
            url: '/Matches/SwitchSides/' + matchId,
            method: 'POST',
            headers: { 'RequestVerificationToken': getAntiForgeryToken() },
            success: function (result) {
                if (result.success) {
                    window.location.reload();
                } else {
                    showError('Could not switch sides.');
                }
            },
            error: function () { showError('Network error.'); }
        });
    };

    // ── Start match ───────────────────────────────────────────────────────────
    window.startMatch = function () {
        $.ajax({
            url: '/Matches/StartMatch/' + matchId,
            method: 'POST',
            headers: { 'RequestVerificationToken': getAntiForgeryToken() },
            success: function (result) {
                if (result.success) {
                    matchStatus = 'InProgress';
                    $('#btnStartMatch').hide();
                    $('#matchStatusText').text('InProgress')
                        .removeClass('bg-secondary bg-dark').addClass('bg-success');
                } else {
                    showError(result.error || 'Could not start match.');
                }
            },
            error: function (xhr) {
                var msg = 'Could not start match.';
                try { msg = JSON.parse(xhr.responseText).error || msg; } catch (e) { }
                showError(msg);
            }
        });
    };

    // ── Error toast ───────────────────────────────────────────────────────────
    function showError(msg) {
        $('#errorToastMessage').text(msg);
        var toastEl = document.getElementById('errorToast');
        if (typeof bootstrap !== 'undefined') {
            var toast = new bootstrap.Toast(toastEl, { delay: 4000 });
            toast.show();
        } else {
            alert(msg);
        }
    }

    // ── Anti-forgery token helper ─────────────────────────────────────────────
    function getAntiForgeryToken() {
        return $('input[name="__RequestVerificationToken"]').val() || '';
    }

    // ── Score Tally Panel ─────────────────────────────────────────────────────
    // Builds the number grid, wires jQuery UI draggable + resizable,
    // auto-checks cells up to the initial (handicap) score on load,
    // and updates whenever a point is scored.

    var TALLY_MAX = 36;   // highest point shown on the tally sheet
    var tallyMinimised = false;

    function initTallyPanel() {
        buildTallyTable();
        restoreTallyPosition();

        // Make panel draggable (by its handle bar)
        $('#scoreTallyPanel').draggable({
            handle: '#tallyHandle',
            containment: 'window',
            stop: saveTallyPosition
        });

        // Make panel resizable on all edges
        $('#scoreTallyPanel').resizable({
            handles: 'all',
            minWidth: 240,
            minHeight: 180,
            stop: saveTallyPosition
        });

        // Apply handicap checks immediately
        var initLeftScore  = homeTeamOnLeft ? COURT_DATA.homeScore : COURT_DATA.awayScore;
        var initRightScore = homeTeamOnLeft ? COURT_DATA.awayScore : COURT_DATA.homeScore;
        updateTallyScores(initLeftScore, initRightScore);

        // Initial next-server display (read player at position 1 from rendered DOM)
        var initLeftIsServing = homeTeamOnLeft ? homeIsServing : !homeIsServing;
        var leftServerInfo  = getServerFromDom('left');
        var rightServerInfo = getServerFromDom('right');
        updateTallyServer(leftServerInfo, rightServerInfo, initLeftIsServing);
    }

    function buildTallyTable() {
        var leftName  = homeTeamOnLeft ? COURT_DATA.homeTeamName : COURT_DATA.awayTeamName;
        var rightName = homeTeamOnLeft ? COURT_DATA.awayTeamName : COURT_DATA.homeTeamName;

        var html = '<thead>';
        // Team name headers
        html += '<tr>' +
            '<th colspan="3" class="tally-team-hdr" id="tallyTeamLeft" title="' + esc(leftName) + '">' + esc(truncate(leftName, 12)) + '</th>' +
            '<th class="tally-sep"></th>' +
            '<th colspan="3" class="tally-team-hdr" id="tallyTeamRight" title="' + esc(rightName) + '">' + esc(truncate(rightName, 12)) + '</th>' +
            '</tr>';
        // "Points" sub-header
        html += '<tr>' +
            '<th class="tally-sub-hdr" colspan="3">Points</th>' +
            '<th class="tally-sep"></th>' +
            '<th class="tally-sub-hdr" colspan="3">Points</th>' +
            '</tr>';
        html += '</thead><tbody>';

        // 12 rows: each row shows N, N+12, N+24 for each side
        for (var n = 1; n <= 12; n++) {
            var c1 = n, c2 = n + 12, c3 = n + 24;
            html += '<tr>' +
                '<td class="tally-num" id="lnum-' + c1 + '">' + c1 + '</td>' +
                '<td class="tally-num" id="lnum-' + c2 + '">' + c2 + '</td>' +
                '<td class="tally-num" id="lnum-' + c3 + '">' + c3 + '</td>' +
                '<td class="tally-sep"></td>' +
                '<td class="tally-num" id="rnum-' + c1 + '">' + c1 + '</td>' +
                '<td class="tally-num" id="rnum-' + c2 + '">' + c2 + '</td>' +
                '<td class="tally-num" id="rnum-' + c3 + '">' + c3 + '</td>' +
                '</tr>';
        }

        html += '</tbody><tfoot>';
        // Time-outs section
        html += '<tr>' +
            '<td colspan="3" class="tally-timeout-hdr">Time Outs</td>' +
            '<td class="tally-sep"></td>' +
            '<td colspan="3" class="tally-timeout-hdr">Time Outs</td>' +
            '</tr>';
        html += '<tr>' +
            '<td colspan="3" class="tally-timeout-cell">' +
                '<span class="tally-to" id="lto-1" onclick="tallyTimeoutClick(this)"></span>' +
                '<span class="tally-to" id="lto-2" onclick="tallyTimeoutClick(this)"></span>' +
            '</td>' +
            '<td class="tally-sep"></td>' +
            '<td colspan="3" class="tally-timeout-cell">' +
                '<span class="tally-to" id="rto-1" onclick="tallyTimeoutClick(this)"></span>' +
                '<span class="tally-to" id="rto-2" onclick="tallyTimeoutClick(this)"></span>' +
            '</td>' +
            '</tr>';
        html += '</tfoot>';

        document.getElementById('tallyTable').innerHTML = html;
    }

    function updateTallyScores(leftScore, rightScore) {
        for (var i = 1; i <= TALLY_MAX; i++) {
            var lEl = document.getElementById('lnum-' + i);
            var rEl = document.getElementById('rnum-' + i);
            if (lEl) lEl.className = 'tally-num' + (i <= leftScore ? ' scored' : '');
            if (rEl) rEl.className = 'tally-num' + (i <= rightScore ? ' scored' : '');
        }
    }

    function updateTallyServer(leftServer, rightServer, leftIsServing) {
        var $left  = $('#tallyServerLeft');
        var $right = $('#tallyServerRight');

        $left.toggleClass('is-serving', !!leftIsServing);
        $right.toggleClass('is-serving', !leftIsServing);

        $('#tallyServerNameLeft').text(leftServer  ? ('#' + leftServer.number + (leftServer.name ? ' ' + leftServer.name : '')) : '–');
        $('#tallyServerNameRight').text(rightServer ? ('#' + rightServer.number + (rightServer.name ? ' ' + rightServer.name : '')) : '–');
    }

    // Read server info for a side from the rendered court DOM
    function getServerFromDom(side) {
        var halfId = side === 'left' ? '#leftHalf' : '#rightHalf';
        var slot = $(halfId + ' .position-slot[data-position="1"]');
        if (slot.hasClass('occupied')) {
            var num  = slot.find('.slot-number').text().trim();
            var name = slot.find('.slot-player-name').text().trim();
            return { number: num, name: name };
        }
        return null;
    }

    // Extract server from a positions array (returned by score endpoint)
    function getServerFromPositions(positions) {
        if (!positions) return null;
        for (var i = 0; i < positions.length; i++) {
            if (positions[i].position === 1 && positions[i].player) {
                return { number: positions[i].player.number, name: positions[i].player.name };
            }
        }
        return null;
    }

    // Persist panel geometry in sessionStorage so it survives set-change reloads
    function saveTallyPosition() {
        try {
            var $p = $('#scoreTallyPanel');
            sessionStorage.setItem('tallyPanel', JSON.stringify({
                left: $p.css('left'), top: $p.css('top'),
                width: $p.outerWidth(), height: $p.outerHeight(),
                minimised: tallyMinimised
            }));
        } catch(e) {}
    }

    function restoreTallyPosition() {
        try {
            var saved = JSON.parse(sessionStorage.getItem('tallyPanel') || 'null');
            if (!saved) return;
            var $p = $('#scoreTallyPanel');
            // Remove initial centering transform before setting absolute offsets
            $p.css({ transform: 'none', left: saved.left, top: saved.top });
            if (saved.width)  $p.outerWidth(saved.width);
            if (saved.height) $p.outerHeight(saved.height);
            if (saved.minimised) {
                tallyMinimised = false;  // toggleTallyContent will flip it
                toggleTallyContent();
            }
        } catch(e) {}
    }

    // Minimise / expand toggle
    window.toggleTallyContent = function () {
        tallyMinimised = !tallyMinimised;
        $('#tallyContent').toggle(!tallyMinimised);
        $('#tallyMinBtn').text(tallyMinimised ? '+' : '−');
        saveTallyPosition();
    };

    // Time-out dot click
    window.tallyTimeoutClick = function (el) {
        el.classList.toggle('used');
    };

    // Utility helpers
    function esc(s) {
        return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
    function truncate(s, n) {
        return s && s.length > n ? s.slice(0, n) + '\u2026' : s;
    }

    // ── SignalR (listen for updates triggered by other operator windows) ──────
    function initSignalR() {
        var connection = new signalR.HubConnectionBuilder()
            .withUrl('/scoreHub')
            .withAutomaticReconnect()
            .build();

        connection.on('ScoreUpdated', function (result) {
            // Only apply if it comes from a different browser tab
            // (our own AJAX calls already applied the update)
            applyScoreUpdate(result);
        });

        connection.start()
            .then(function () {
                connection.invoke('JoinMatchGroup', matchId).catch(console.error);
            })
            .catch(function (err) {
                console.warn('SignalR connection failed:', err.toString());
            });
    }

    // ── Initialise ────────────────────────────────────────────────────────────
    $(document).ready(function () {
        initDragDrop();
        initSignalR();
        initTallyPanel();

        // Set initial serving highlight on position 1
        var initServingLeft = homeTeamOnLeft ? homeIsServing : !homeIsServing;
        updateServingBadge(initServingLeft);
    });

})();
