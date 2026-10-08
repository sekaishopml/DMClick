**3. Instalación.**
Entrar a la carpeta DMClick dar doble click en **INSTALAR.cmd**.

va a salir avisos administrador (ACCEPTAR).

**4. Reiniciar.**
El driver solo empieza a funcionar después de reiniciar.

**5. Uso.**
Después de reiniciar, DMClick arranca solo y aparece en el adm de tareas

Lo primero que se hace es mover el mouse principal luego se queda guardado. El otro mouse va a ser la flecha naranja.

## Opciones en barra de tarea

click derecho al icono de DMClick van a ver 3 opciones.

**Elegir mouse principal**, para cambiar de mouse principal.

**Abrir configuración**, para cambiar la velocidad del segundo mouse. Se cambia velocidad2 (1.0 es normal, 1.3 más rápido, 0.8 más lento), se tiene que guardar y volcer abrir DMClick.

**Salir**, Ctrl + Alt + Q (combinación de teclado temporal.

## Para actualizarlo

Si hago cambios en el programa, se tiene que abrir PowerShell y pegar:

`cd $HOME\Desktop\DMClick`

`git pull`

Y le das doble click a INSTALAR.cmd otra vez. Esta vez ya no instala .NET ni el driver porque ya están, solo actualiza el programa.

## Si algo falla

Si al abrirlo dice que el driver no está activo, reiniciar la computadora. Si sigue igual, corre INSTALAR.cmd otra vez si no ya fue.

Si se conecta y desconecta el mouse muchas veces seguidas sin reiniciar, en algún momento pueden dejar de valer.

Si Windows o el antivirus te avisa que el programa no es conocido, dale a Más información y luego a Ejecutar de todas formas. Pasa porque el programa no tiene firma digital xk aún no está terminado.

SE TIENE QUE INICIAR COMO ADMINISTRADOR =)

## Cosas que no se pueden hacer

No se puede arrastrar con los dos mouses al mismo tiempo. Windows solo tiene un cursor de verdad no se puede más el de aquí solo está virtualizado, así que mientras uno está arrastrando algo, los clicks del otro se ignoran hasta que sueltes del otro mouse. Su flecha sí se sigue moviendo.

Cuando abres un menú con el segundo mouse, las opciones no se iluminan al pasar la flecha naranja por encima, pero el click sí funciona =).

