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
    var isDoubles   = COURT_DATA.isDoubles === true;
    var pointsToWin = COURT_DATA.pointsToWin || 25;

    // Divisor for the switch-sides cue: every N total points
    var switchSidesEvery = pointsToWin === 11 ? 4
                         : pointsToWin === 15 ? 5
                         : pointsToWin === 21 ? 7
                         : 0; // no cue for 25-pt indoor

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

        // Switch-sides cue: show when total score hits the interval for the set length
        if (switchSidesEvery > 0) {
            var total = result.homeScore + result.awayScore;
            if (total > 0 && total % switchSidesEvery === 0) {
                showSwitchSidesCue();
            } else {
                hideSwitchSidesCue();
            }
        }

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

    // ── Manual rotation ───────────────────────────────────────────────────────
    window.rotateTeam = function (side, direction) {
        $.ajax({
            url: '/Score/RotateTeam',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ matchId: matchId, team: side, direction: direction }),
            success: function (result) {
                if (!result.success) {
                    showError(result.error || 'Rotation failed.');
                    return;
                }
                var leftIsServing = homeTeamOnLeft ? result.homeIsServing : !result.homeIsServing;
                if (result.homeCourtPositions && result.awayCourtPositions) {
                    var leftPos  = homeTeamOnLeft ? result.homeCourtPositions : result.awayCourtPositions;
                    var rightPos = homeTeamOnLeft ? result.awayCourtPositions : result.homeCourtPositions;
                    renderCourtPositions('left',  leftPos,   leftIsServing);
                    renderCourtPositions('right', rightPos, !leftIsServing);
                }
            },
            error: function () { showError('Network error during rotation.'); }
        });
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

    // ── Switch-sides cue helpers ─────────────────────────────────────────────
    function showSwitchSidesCue() {
        $('#switchSidesCue').show();
    }

    function hideSwitchSidesCue() {
        $('#switchSidesCue').hide();
    }

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

        // Set initial serving highlight on position 1
        var initServingLeft = homeTeamOnLeft ? homeIsServing : !homeIsServing;
        updateServingBadge(initServingLeft);

        // Check initial score for switch-sides cue
        if (switchSidesEvery > 0) {
            var initTotal = homeScore + awayScore;
            if (initTotal > 0 && initTotal % switchSidesEvery === 0) showSwitchSidesCue();
        }
    });

})();
