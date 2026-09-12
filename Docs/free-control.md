# Free Control

The default-off **Free control (playground)** toggle is in Ninja Slayer settings under **Hidden features**. It can be changed from the main menu or a paused run. It applies only to the single-player Ninja Slayer during the player's Play phase. Free transforms are not saved.

| Input | Action |
| --- | --- |
| A / D | Run, with air steering |
| W / Space | Jump; release early for a lower jump; press again for one air jump |
| S | Fast fall |
| Shift | Dash; no invulnerability |
| Hold toward a wall, then jump | Wall slide and wall jump |
| Click empty battlefield | Light attack, 6 damage on contact |
| Hold left mouse 0.15-0.5 seconds, release | Heavy attack, 12 damage on contact |
| Hold left mouse for 0.5 seconds | Empowered Tornado charge followed by 0.8 seconds of movable spinning; 4 damage per contact |
| Right click | One rotating shuriken, 6 damage, minimum 0.167-second press interval |
| Hold a solid part of the body and drag | Passive physical grip at the clicked point; gravity makes the body hang freely |

Dragging does not generate an automatic swing. Cursor motion supplies the force; releasing preserves linear and angular momentum. Keyboard movement resumes an upright walking posture. Scarf and smoke are excluded from the physical hull. The initial body floor spans the battlefield, with walls and a ceiling at the viewport edges. Enemies stay in place and act as rigid obstacles for the thrown body.

Contact speed includes angular motion around the center of mass. Below 200 px/s it deals no collision damage; otherwise damage is `clamp(floor((speed / 200)^2), 1, 100)`. A target can receive physical/Tornado contact damage at most once every 0.15 seconds. A simultaneous fixed strike and physical impact use the larger value. Card animation travel is not included in physical velocity. Shuriken use the first enemy crossed by their straight path and can miss.

Free attacks consume no cards, energy, or shuriken stock. Cards retain their normal gameplay queue and compose their visuals with free movement. Cinematic finishers and Alabama Drop temporarily suspend control and return to the saved free position. Ending the turn, disabling the setting, dying, or leaving combat clears free actions and restores the authored battle placement. Overlays, pause, and loss of focus suspend controls.

Movement and free actions use real time regardless of combat animation speed. Movement tuning: run 420 px/s, jump 850 px/s, gravity 1800 px/s², dash 1200 px/s for 0.18 seconds, coyote time 0.08 seconds and jump buffer 0.10 seconds.
