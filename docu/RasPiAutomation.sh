#!/bin/bash
#

case "$1" in
  0)
    killall -SIGKILL RasPiAutomation
;;
   00)
    killall -SIGKILL RasPiAutomation
;;
   10)
    sudo chmod 777 -c -R remote-debugging
    sudo nice --15  remote-debugging/RasPiAutomation $1 $2 &
;;
   11)
    sudo chmod 777 -c -R remote-debugging
    sudo nice --15  remote-debugging/RasPiAutomation  $1 $2 &
;;
   20)
    sudo chmod 777 -c -R remote-debugging
    sudo nice --15  remote-debugging/RasPiAutomation $1 $2 &
;;
   21)
    sudo chmod 777 -c -R remote-debugging
    sudo nice --15  remote-debugging/RasPiAutomation  $1 $2 &
;;
   30)
    sudo chmod 777 -c -R remote-debugging
    sudo nice --15  remote-debugging/RasPiAutomation  $1 $2 &
;;
   31)
   sudo chmod 777 -c -R remote-debugging
   sudo nice --15  remote-debugging/RasPiAutomation   $1 $2 &
;;
   40)
   sudo  chmod 777 -c -R remote-debugging
   sudo nice --15   remote-debugging/RasPiAutomation  $1 $2 &
;;
   41)
   sudo chmod 777 -c -R remote-debugging
   sudo nice --15  remote-debugging/RasPiAutomation  $1 $2 &
;;
   50)
   sudo  chmod 777 -c -R remote-debugging
   val = sudo nice --15   remote-debugging/RasPiAutomation  $1 $2 &
   if[$val -eq 99]
   then
	sudo shutdown -h now
   else
	echo returnval = $val
   fi
      
;;  
   *)
    echo "Ivalid parameter!!! Values 0 - 19 are valid"
;;
esac
