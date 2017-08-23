# Troubleshooting Tips for Android Publishing

## Serve on WiFi Network

### Problem: Android Device is not seeing book advertisements

When using this method, you need to run Bloom Reader, open its menu, and choose "Receive from WiFi".

1. Make sure the device is in the "Receive from WiFi" screen
2. Make sure the computer and device are on the same WiFi network. A computer plugged in via cable to the WiFi router still may be on its own network. It is technically possible to connect wired networks to be able to be on the same local subnet as WiFi, but the Bloom team will not be able to provide any help in setting that up, sorry.
3. Make sure your computer doesn't have a firewall that is interfering. The windows Firewall will display when you first start advertising a book. If you said "No" to that, it might not ever ask you again. If you have additional 3rd party firewalls installed, try disabling them. Bloom advertises a book by broadcasting on the local net via UDP on port 5913, and Bloom Reader requests a book via port 5915.


## Send Over USB Cable

### Problem: Bloom is not connecting to my device

> We have found this method to be problematic with many devices, so consider using the WiFi method even if you are only working with a single device.

When using this method, you do not have to go into any special mode to receive books. Just run Bloom Reader. Books should automatically appear when they are transferred.

1. Some Android devices have multiple modes they can be in when connected to a computer. Some will ask you as soon as you connect the cable to the computer. Bloom will be trying to talk to the device using a protocol called "MTP". If you're not sure that you're device is connecting using MTP, try googling the name of your device + "MTP".
2. The device should be showing up in Windows File Explorer (this feature is Windows-only)
3. Try a different cable, or a different USB port.
