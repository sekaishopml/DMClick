# DMClick

Hice este programa porque quería usar dos mouse en la misma computadora y que cada uno tuviera su propia flechita, como cuando alguien se conecta por AnyDesk y ves su puntero moviéndose aparte del tuyo.

Tu mouse principal sigue siendo el cursor normal de Windows. El segundo mouse aparece como una flecha naranja con un "2" al lado. Los dos se pueden mover al mismo tiempo sin que nada parpadee, y los dos pueden hacer click, click derecho y usar la rueda. Si das click derecho con uno y luego con el otro en otro lado, el menú no se duplica, simplemente se cierra el primero y se abre donde está el segundo.

Si desconectas el segundo mouse, su flecha desaparece. Si lo vuelves a conectar, aparece otra vez apenas lo muevas.

## Lo que necesitas

Una computadora con Windows 10 o Windows 11 de 64 bits. No funciona en las computadoras con procesador ARM (algunas Surface y laptops Snapdragon).

También necesitas internet la primera vez y poder aceptar los avisos de administrador.

## Cómo instalarlo en otra computadora, paso a paso

**1. Instala Git.**
Abre el menú Inicio, escribe PowerShell y ábrelo. Pega esto y dale Enter:

`winget install --id Git.Git -e`

Cuando termine, cierra PowerShell y ábrelo de nuevo, para que reconozca Git.

Si te dice que winget no existe, descarga Git desde https://git-scm.com/download/win y lo instalas dándole siguiente a todo.

**2. Descarga el programa.**
En la nueva ventana de PowerShell pega esto, una línea a la vez:

`cd $HOME\Desktop`

`git clone https://github.com/sekaishopml/DMClick.git`

Te va a quedar una carpeta llamada DMClick en el Escritorio.

**3. Instálalo.**
Entra a la carpeta DMClick y dale doble click a **INSTALAR.cmd**.

Eso hace todo solo. Revisa si la computadora ya tiene lo necesario y solo instala lo que falta. Primero instala .NET 8 si no está, que es lo que se usa para armar el programa (tarda unos minutos la primera vez). Después compila DMClick, lo copia a Archivos de programa, pone un acceso directo en el Escritorio y hace que arranque solo cuando prendes la computadora. Por último instala el driver Interception, que es lo que permite saber cuál mouse es cuál.

Te va a salir uno o dos avisos de administrador, acéptalos.

**4. Reinicia.**
Si el instalador te pide reiniciar, hazlo. El driver solo empieza a funcionar después de reiniciar.

**5. Úsalo.**
Después de reiniciar, DMClick arranca solo y aparece su icono al lado del reloj. Si no arrancó, ábrelo desde el acceso directo del Escritorio.

Lo primero que tienes que hacer es mover tu mouse principal, el que quieres que sea el cursor normal. Se queda guardado. El otro mouse va a ser la flecha naranja.

## Cosas que puedes hacer desde el icono del reloj

Dale click derecho al icono de DMClick al lado del reloj y vas a ver tres opciones.

**Elegir mouse principal**, por si te equivocaste de mouse. Después de darle, mueve el que quieres como principal.

**Abrir configuración**, para cambiar la velocidad del segundo mouse. Cambia el número de speed2 (1.0 es normal, 1.3 más rápido, 0.8 más lento), guarda y vuelve a abrir DMClick.

**Salir**, que cierra el programa. También puedes cerrarlo con Ctrl + Alt + Q.

## Para actualizarlo

Si subo cambios, en la otra computadora abres PowerShell y pegas:

`cd $HOME\Desktop\DMClick`

`git pull`

Y le das doble click a INSTALAR.cmd otra vez. Esta vez ya no instala .NET ni el driver porque ya están, solo actualiza el programa.

## Para desinstalarlo

Ve a Configuración, luego Aplicaciones, busca DMClick y dale a Desinstalar. Te va a preguntar si también quieres quitar el driver. Si dices que sí, reinicia al final.

## Si algo falla

Si al abrirlo dice que el driver no está activo, reinicia la computadora. Si sigue igual, corre INSTALAR.cmd otra vez.

En Windows 11, si tienes activada la opción Integridad de memoria (en Seguridad de Windows, Seguridad del dispositivo, Aislamiento del núcleo), puede que bloquee el driver. Si pasa eso, desactívala y reinicia.

Si conectas y desconectas mouse muchas veces seguidas sin reiniciar, en algún momento pueden dejar de responder. Reiniciando se arregla. Es una limitación conocida del driver.

Si Windows o el antivirus te avisa que el programa no es conocido, dale a Más información y luego a Ejecutar de todas formas. Pasa porque el programa no tiene firma digital.

Si quieres que el segundo mouse pueda hacer click en programas abiertos como administrador, también tienes que abrir DMClick como administrador.

## Cosas que no se pueden hacer

No se puede arrastrar con los dos mouse al mismo tiempo. Windows solo tiene un cursor de verdad, así que mientras uno está arrastrando algo, los clicks del otro se ignoran hasta que sueltes. Su flecha sí se sigue moviendo.

Cuando abres un menú con el segundo mouse, las opciones no se iluminan al pasar la flecha naranja por encima, pero el click sí funciona.

## Créditos

El driver que hace posible esto es Interception, de Francisco Lopes da Silva (https://github.com/oblitum/Interception), con licencia LGPL 3.0. Viene incluido en la carpeta third_party.
