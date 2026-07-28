#!/bin/bash

APP="JokiAutomation/RasPiAutomation"

init_gpio()
{
    for pin in 5 6 7 13 19 26 16 20 21
    do
        pigs m "$pin" w
        pigs w "$pin" 0
    done
}

start_app()
{
    sudo chmod 755 "$APP"
    sudo nice -15 "$APP" "$1" "$2" &
}

case "$1" in

0)
    init_gpio

    sudo chmod 666 /dev/ttyUSB0
    stty -F /dev/ttyUSB0 9600 raw -echo

    echo -n '(MX*:RES!)' > /dev/ttyUSB0

    pkill -9 RasPiAutomation
    ;;

00)
    pkill -9 RasPiAutomation
    ;;

10|11|20|21|30|31|40|41|42|43|44|52)
    start_app "$1" "$2"
    ;;

50)
    start_app "$1" "$2"

    if [ "$2" = "15" ]; then
        shutdown -h now
    fi
    ;;

*)
    echo "Invalid parameter!"
    echo "Valid values: 0, 00, 10, 11, 20, 21, 30, 31, 40, 41, 42, 43, 44, 50, 52"
    exit 1
    ;;

esac

exit 0

