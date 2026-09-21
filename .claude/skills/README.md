# Acá NO viven las skills — y eso es el arreglo, no una falta

El arnés vive en **un** repo, [`Synergos.Fabrica`](https://github.com/CHERCED-DEV/Synergos.Fabrica),
y este repo lo **fija por SHA** en `arnes.lock.json` (raíz).

## Por qué

Estaba copiado en los dos árboles. Medido el 2026-09-21: de las 21 skills compartidas, **18
eran byte a byte idénticas y 4 ya habían divergido**. Nadie decidió que divergieran — alguien
arregló una y no fue a la otra, que es `the_same_algorithm_is_not_the_same_thing` con el
agravante de la regla 26 del repo hermano: **el que miente no es el que se arregló**.

La cuarta divergencia la causó la medición anterior: el arreglo del pin de Umbraco (#149)
tocó `guardrails` y `media-upload` en el CMS y no acá. Por eso la salida no fue cruzar las
copias — fue borrarlas.

## Cómo se instala

```
/plugin marketplace add https://github.com/CHERCED-DEV/Synergos.Fabrica.git#<el sha del lock>
/plugin install synergos-fabrica@synergos-fabrica
```

## Y esta carpeta sigue existiendo por algo

Es donde iría una skill **propia de este repo**, si alguna vez hace falta una que no tenga
sentido en el otro árbol. Hoy no hay ninguna: todo lo que había era compartido, y por eso
estaba duplicado.

Ver `CHERCED-DEV/Synergos.CMS#141` y la épica `#139`.
