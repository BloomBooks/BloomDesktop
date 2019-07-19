# Troubleshooting Tips for Android Publishing {i18n="android.publish.trouble.tips"}

## Share over Wi-Fi {i18n="android.publish.wifi"}

### Problem: Android Device is not seeing book advertisements {i18n="android.publish.wifi.unseen"}

When using this method, you need to run Bloom Reader, open its menu, and choose "Receive from Wi-Fi". {i18n="android.publish.wifi.run.receive"}

1. Make sure the device is in the "Receive from Wi-Fi" screen {i18n="android.publish.wifi.receive.from"}
2. Make sure the computer and device are on the same Wi-Fi network. A computer plugged in via cable to the Wi-Fi router still may be on its own network. It is technically possible to connect wired networks to be able to be on the same local subnet as Wi-Fi, but the Bloom team will not be able to provide any help in setting that up, sorry. {i18n="android.publish.wifi.same.network"}
3. Make sure your computer doesn't have a firewall that is interfering. The windows Firewall will display when you first start advertising a book. If you said "No" to that, it might not ever ask you again. If you have additional 3rd party firewalls installed, try disabling them. Bloom advertises a book by broadcasting on the local net via UDP on port 5913, and Bloom Reader requests a book via port 5915. {i18n="android.publish.wifi.firewalls"}


## Send Over USB Cable {i18n="android.publish.usb"}

### Problem: Bloom is not connecting to my device {i18n="android.publish.usb.not.connect"}

> We have found this method to be problematic with many devices, so consider using the Wi-Fi method even if you are only working with a single device. {i18n="android.publish.usb.consider.wifi"}

When using this method, you do not have to go into any special mode to receive books. Just run Bloom Reader. Books should automatically appear when they are transferred. {i18n="android.publish.usb.automatic"}

1. Some Android devices have multiple modes they can be in when connected to a computer. Some will ask you as soon as you connect the cable to the computer. Bloom will be trying to talk to the device using a protocol called "MTP". If you're not sure that your device is connecting using MTP, try googling the name of your device + "MTP". {i18n="android.publish.usb.uses.mtp"}
2. The device should be showing up in Windows File Explorer (this feature is Windows-only) {i18n="android.publish.usb.windows"}
3. Try a different cable, or a different USB port. {i18n="android.publish.usb.cable.or.port"}
