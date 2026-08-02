#!/bin/bash
set -euo pipefail

LOG="/home/pi/autostart.log"
echo "===== AutoStart: $(date) =====" >> "$LOG"

# use one of autostart modes
# MODE="MeasureSwitch"
# MODE="PSD"
# MODE="JokiAutomation"
MODE="RasPiFlipperGateway"
# MODE="NONE"

case "$MODE" in
  MeasureSwitch)
    echo "[AutoStart] Start MeasureSwitch.start" >> "$LOG"
    cd /home/pi/MeasureSwitch
    exec /bin/bash /home/pi/MeasureSwitch/Scripts/MeasureSwitch.start
    ;;

  PSD)
    echo "[AutoStart] Starte PSD.sh 00" >> "$LOG"
    cd /home/pi/PSD
    exec /bin/bash /home/pi/PSD/PSD.sh 00
    ;;

  JokiAutomation)
    echo "[AutoStart] Starte RasPiAutomation.start" >> "$LOG"
    cd /home/pi/JokiAutomation/scripts
    exec /bin/bash /home/pi/JokiAutomation/scripts/RasPiAutomation.start
    ;;

  RasPiFlipperGateway)
    echo "[AutoStart] Starte RasPiFlipperGateway.start" >> "$LOG"
    cd /home/pi/raspi_flipper_gateway/Scripts
    exec /bin/bash /home/pi/raspi_flipper_gateway/Scripts/RasPiFlipperGateway.start
    ;;
    
  NONE)
    echo "[AutoStart] MODE=NONE – kein Autostart" >> "$LOG"
    exit 0
    ;;

  *)
    echo "[AutoStart] Unbekannter MODE=$MODE" >> "$LOG"
    exit 1
    ;;
esac
