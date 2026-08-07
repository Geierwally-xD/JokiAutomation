# Canon XC `control.cgi` Parameter Report (Appendix iii)

## Scope
Requested commands:
- Pan left start
- Pan right start
- Tilt up start
- Tilt down start
- Zoom in start
- Zoom out start
- Pan stop
- Tilt stop
- Zoom stop

## Result
I could not find the Canon XC Control Protocol specification (Appendix iii) in the current workspace, so exact `control.cgi` parameter names/values for these PTZ commands cannot be confirmed yet.

| Command | Parameter name | Value range | Command example | Source section |
|---|---|---|---|---|
| Pan left start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Pan right start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Tilt up start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Tilt down start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Zoom in start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Zoom out start | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Pan stop | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Tilt stop | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |
| Zoom stop | **Not confirmed** | **Not confirmed** | `control.cgi?s=<SessionID>&<name>=<value>` | Canon XC Protocol Spec, Appendix iii (not available in workspace) |

## Notes
- No PTZ parameter mapping was implemented or changed.
- Existing TODO behavior in `XcCanonPtzController.cs` remains unchanged.
