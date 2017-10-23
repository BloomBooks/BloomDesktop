# Consejos para la Solución de Problemas de Publicación de Android

## Compartir en red WiFi

### Problema: El dispositivo Android no está recibiendo anuncios del libro

Cuando se utiliza este método, necesita ejecutar el Lector Bloom. Abra el menú y elija "Recibir de WiFi".

1. Asegúrese que el dispositivo esté en la pantalla de "Recibir de WiFi"
2. Asegúrese que la computadora y el dispositivo estén en la misma red WiFi. Una computadora conectada por cable al enrutador WiFi todavía puede estar en su propia red. Es técnicamente posible conectar redes por cable para poder estar en la misma subred local como WiFi, pero los desarolladores de Bloom no pueden ayudarle a configurar esto.
3. Asegúrese que su computadora no tenga un contrafuegos (firewall) que está interfiriendo. El contrafuegos de Windows se mostrará cuando recién empiece a compartir su libro. Si dijo "No" a eso, es posible que no vuelva a preguntarle. Si tiene otros contrafuegos instalados, intente deshabilitarlos. Bloom comparte un libro transmitiéndolo en la red local a través de UDP en el puerto 5913 y el Lector Bloom pide un libro por medio del puerto 5915.


## Enviar a través del cable USB

### Problema: Bloom no está conectando a mi dispositivo

> Hemos encontrado que este método es problemático para muchos dispositivos. Por lo tanto, considere el método WiFi incluso si está trabajando solamente con un dispositivo.

Mientras que utiliza este método, no tiene que entrar en ningún modo especial para recibir libros. Basta con ejecutar el Lector Bloom. Los libros deben aparecer automáticamente cuando se transfieran.

1. Algunos dispositivos Android pueden estar en varios modos cuando están conectados a una computadora. Algunos le preguntarán tan pronto como usted conecte el cable a la computadora. Bloom estará tratando de comunicarse con el dispositivo mediante un protocolo llamado "MTP". Si no está seguro que su dispositivo está conectando mediante MTP, intente buscar en Google el nombre de su dispositivo y "MTP".
2. El dispositivo debe mostrarse en el Explorador de Archivos de Windows (esta característica se encuentra solamente en Windows)
3. Pruebe un cable diferente o un puerto USB diferente.
