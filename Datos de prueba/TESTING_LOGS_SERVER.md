# Guía de Testing del Servidor de Logs

Esta guía te ayudará a crear datos de prueba desde el cliente y testear todos los endpoints del servidor de logs.

## Prerequisitos

1. **Servidor principal** corriendo (puerto 5000)
2. **Servidor de logs** corriendo (puerto 5001)
3. **RabbitMQ** corriendo (puerto 5672)
4. **Cliente** listo para ejecutar

---

## Parte 1: Crear Datos de Prueba desde el Cliente

### Paso 1: Crear Todos los Usuarios

Ejecuta el cliente y crea los tres usuarios de una vez:

```
1. Register
   - Username: alice
   - Password: alice

2. Register
   - Username: bob
   - Password: bob

3. Register
   - Username: charlie
   - Password: charlie
```

**Logs generados:**
- `Usuario registrado` (alice)
- `Usuario registrado` (bob)
- `Usuario registrado` (charlie)

---

### Paso 2: Usuario ALICE - Todas sus Acciones

Ahora haz **todas** las acciones de alice de una vez:

```
1. Login
   - Username: alice
   - Password: alice

2. Create Class
   - Name: Yoga Morning
   - Description: Morning yoga session
   - Duration: 60
   - Max places: 10
   - Date: (presionar enter para fecha actual)
   - Time: 09:00
   - Image: n (opcional)

[En caso de querer testear el webhook, verificar tambien que la fecha y hora esten unos minutos adelantados a la actual]

3. Join Class
   - Selecciona: Yoga Morning (su propia clase)
   - Webhook URL: (opcional)

4. Logout (opción 7)
```

**Logs generados:**
- `Usuario inició sesión` (alice)
- `Clase creada` (Yoga Morning por alice)
- `Usuario se inscribió a clase` (alice a Yoga Morning)

---

### Paso 3: Usuario BOB - Todas sus Acciones

Ahora haz **todas** las acciones de bob de una vez:

```
1. Login
   - Username: bob
   - Password: bob

2. Create Class
   - Name: Pilates Evening
   - Description: Evening pilates class
   - Duration: 45
   - Max places: 8
   - Date: (presiona Enter para fecha actual)
   - Time: 18:00
   - Image: n (opcional)

[En caso de querer testear el webhook, verificar tambien que la fecha y hora esten unos minutos adelantados a la actual]

3. Join Class
   - Selecciona: Yoga Morning (creada por alice)
   - Webhook URL: (opcional)

4. Join Class
   - Selecciona: Yoga (clase por defecto)
   - Webhook URL: (opcional)

5. Join Class
   - Selecciona: Pilates Evening (su propia clase)
   - Webhook URL: (opcional)

6. Logout (opción 7)
```

**Logs generados:**
- `Usuario inició sesión` (bob)
- `Clase creada` (Pilates Evening por bob)
- `Usuario se inscribió a clase` (bob a Yoga Morning)
- `Usuario se inscribió a clase` (bob a Yoga)
- `Usuario se inscribió a clase` (bob a Pilates Evening)

---

### Paso 4: Usuario CHARLIE - Todas sus Acciones

Ahora haz **todas** las acciones de charlie de una vez:

```
1. Login
   - Username: charlie
   - Password: charlie

2. Create Class
   - Name: CrossFit Training
   - Description: High intensity training
   - Duration: 50
   - Max places: 15
   - Date: (presiona Enter para fecha actual)
   - Time: 10:00
   - Image: n (opcional)

[En caso de querer testear el webhook, verificar tambien que la fecha y hora esten unos minutos adelantados a la actual]

3. Join Class
   - Selecciona: Yoga Morning (creada por alice)
   - Webhook URL: (opcional)

4. Join Class
   - Selecciona: CrossFit Training (su propia clase)
   - Webhook URL: (opcional)

5. Join Class
   - Selecciona: Pilates Evening (creada por bob)
   - Webhook URL: (opcional)

6. Logout (opción 7)
```

**Logs generados:**
- `Usuario inició sesión` (charlie)
- `Clase creada` (CrossFit Training por charlie)
- `Usuario se inscribió a clase` (charlie a Yoga Morning)
- `Usuario se inscribió a clase` (charlie a CrossFit Training)
- `Usuario se inscribió a clase` (charlie a Pilates Evening)

---

### Resumen de Logs Generados

Al final de todos los pasos, deberías tener:

- **3 logs** de `Usuario registrado` (alice, bob, charlie)
- **3 logs** de `Usuario inició sesión` (uno por cada usuario)
- **3 logs** de `Clase creada` (Yoga Morning, Pilates Evening, CrossFit Training)
- **7 logs** de `Usuario se inscribió a clase`:
  - alice → Yoga Morning
  - bob → Yoga Morning
  - bob → Yoga
  - bob → Pilates Evening
  - charlie → Yoga Morning
  - charlie → CrossFit Training
  - charlie → Pilates Evening

**Total: 16 logs**

---

## Parte 2: Endpoints del Servidor de Logs

Base URL: `http://localhost:5001`

### 1. Health Check

**Endpoint:** `GET /health`

**Request:**
```bash
curl http://localhost:5001/health
```

**Response:**
```
OK
```

---

### 2. Obtener Información de Filtros

**Endpoint:** `GET /api/logs/filters`

**Request:**
```bash
curl http://localhost:5001/api/logs/filters
```

**Response:**
```json
{
  "description": "Available filters for the /logs endpoint",
  "filters": [
    {
      "name": "user",
      "type": "string",
      "description": "Filter by username (partial search, case-insensitive)"
    },
    {
      "name": "class",
      "type": "string",
      "description": "Filter by class name (partial search, case-insensitive)"
    },
    {
      "name": "dateFrom",
      "type": "datetime",
      "description": "Filter logs from this date (format: dd/MM/yyyy or dd/MM/yyyy HH:mm)"
    },
    {
      "name": "dateTo",
      "type": "datetime",
      "description": "Filter logs until this date (format: dd/MM/yyyy or dd/MM/yyyy HH:mm)"
    },
    {
      "name": "event",
      "type": "string",
      "description": "Filter by event type (partial search, case-insensitive)"
    }
  ],
  "example": "/logs?user=admin&class=Yoga&dateFrom=01/01/2024&event=enrollment",
  "note": "All filters are optional and can be combined. Results are ordered by date descending."
}
```

---

### 3. Obtener Estadísticas

**Endpoint:** `GET /api/logs/stats`

**Request:**
```bash
curl http://localhost:5001/api/logs/stats
```

**Response:**
```json
{
  "totalLogs": 15,
  "logsByUser": {
    "alice": 5,
    "bob": 6,
    "charlie": 4
  },
  "logsByClass": {
    "Yoga Morning": 4,
    "Pilates Evening": 3,
    "CrossFit Training": 3,
    "": 5
  },
  "logsByEvent": {
    "Usuario registrado": 3,
    "Usuario inició sesión": 3,
    "Clase creada": 3,
    "Usuario se inscribió a clase": 5,
    "Usuario abandonó clase": 1
  },
  "oldestDate": "25/11/2025 - 09:00",
  "newestDate": "25/11/2025 - 18:30"
}
```

---

### 4. Obtener Logs (con Filtros)

**Endpoint:** `GET /api/logs`

**Parámetros de Query:**
- `user` (string): Filtrar por nombre de usuario (búsqueda parcial, case-insensitive)
- `class` (string): Filtrar por nombre de clase (búsqueda parcial, case-insensitive)
- `dateFrom` (datetime): Filtrar desde esta fecha (formato: dd/MM/yyyy o dd/MM/yyyy HH:mm)
- `dateTo` (datetime): Filtrar hasta esta fecha (formato: dd/MM/yyyy o dd/MM/yyyy HH:mm)
- `event` (string): Filtrar por tipo de evento (búsqueda parcial, case-insensitive)

**Response:**
```json
[
  {
    "user": "charlie",
    "class": "Pilates Evening",
    "event": "Usuario se inscribió a clase",
    "date": "25/11/2025 - 18:30",
    "details": "Usuario charlie se inscribió a la clase Pilates Evening"
  },
  {
    "user": "bob",
    "class": "Pilates Evening",
    "event": "Usuario se inscribió a clase",
    "date": "25/11/2025 - 18:15",
    "details": "Usuario bob se inscribió a la clase Pilates Evening"
  }
]
```

---

## Parte 3: Ejemplos de Filtros

### Filtro por Usuario

**Request:**
```bash
curl "http://localhost:5001/api/logs?user=alice"
```

**Obtiene:** Todos los logs del usuario "alice"

**Ejemplo con búsqueda parcial:**
```bash
curl "http://localhost:5001/api/logs?user=ali"
```
Esto encontrará usuarios que contengan "ali" (alice, alicia, etc.)

---

### Filtro por Clase

**Request:**
```bash
curl "http://localhost:5001/api/logs?class=Yoga"
```

**Obtiene:** Todos los logs relacionados con clases que contengan "Yoga"

**Ejemplo:**
```bash
curl "http://localhost:5001/api/logs?class=Morning"
```
Encontrará "Yoga Morning"

---

### Filtro por Evento

**Request:**
```bash
curl "http://localhost:5001/api/logs?event=inscribió"
```

**Obtiene:** Todos los logs de eventos que contengan "inscribió" (inscripciones)

**Otros ejemplos:**
```bash
# Registros
curl "http://localhost:5001/api/logs?event=registrado"

# Inicios de sesión
curl "http://localhost:5001/api/logs?event=sesión"

# Creación de clases
curl "http://localhost:5001/api/logs?event=creada"
```

---

### Filtro por Fecha (Desde)

**Request:**
```bash
curl "http://localhost:5001/api/logs?dateFrom=25/11/2025"
```

**Obtiene:** Todos los logs desde el 25 de noviembre de 2025

**Con hora:**
```bash
curl "http://localhost:5001/api/logs?dateFrom=25/11/2025%2010:00"
```

Nota: En URLs, el espacio debe codificarse como `%20` o usar comillas en curl:
```bash
curl "http://localhost:5001/api/logs?dateFrom=25/11/2025 10:00"
```

---

### Filtro por Fecha (Hasta)

**Request:**
```bash
curl "http://localhost:5001/api/logs?dateTo=25/11/2025"
```

**Obtiene:** Todos los logs hasta el 25 de noviembre de 2025

**Con hora:**
```bash
curl "http://localhost:5001/api/logs?dateTo=25/11/2025%2018:00"
```

O con comillas:
```bash
curl "http://localhost:5001/api/logs?dateTo=25/11/2025 18:00"
```

---

### Filtro por Rango de Fechas

**Request:**
```bash
curl "http://localhost:5001/api/logs?dateFrom=25/11/2025&dateTo=26/11/2025"
```

**Obtiene:** Todos los logs entre el 25 y 26 de noviembre de 2025

---

## Parte 4: Combinación de Filtros

### Múltiples Filtros - Ejemplo 1

**Request:**
```bash
curl "http://localhost:5001/api/logs?user=alice&event=inscribió"
```

**Obtiene:** Todas las inscripciones del usuario "alice"

---

### Múltiples Filtros - Ejemplo 2

**Request:**
```bash
curl "http://localhost:5001/api/logs?class=Yoga&event=creada"
```

**Obtiene:** Todos los eventos de creación de clases que contengan "Yoga"

---

### Múltiples Filtros - Ejemplo 3

**Request:**
```bash
curl "http://localhost:5001/api/logs?user=bob&class=Pilates&dateFrom=25/11/2025"
```

**Obtiene:** Logs del usuario "bob" relacionados con clases que contengan "Pilates" desde el 25 de noviembre

---

### Múltiples Filtros - Ejemplo 4

**Request:**
```bash
curl "http://localhost:5001/api/logs?event=inscribió&dateFrom=25/11/2025%2009:00&dateTo=25/11/2025%2018:00"
```

O con comillas:
```bash
curl "http://localhost:5001/api/logs?event=inscribió&dateFrom=25/11/2025 09:00&dateTo=25/11/2025 18:00"
```

**Obtiene:** Todas las inscripciones entre las 9:00 y 18:00 del 25 de noviembre

---

### Múltiples Filtros - Ejemplo 5 (Completo)

**Request:**
```bash
curl "http://localhost:5001/api/logs?user=charlie&class=CrossFit&event=inscribió&dateFrom=25/11/2025&dateTo=27/11/2025"
```

**Obtiene:** Inscripciones del usuario "charlie" a clases que contengan "CrossFit" entre el 25 y 27 de noviembre

---

## Parte 5: Casos de Uso Comunes

### Ver todas las inscripciones de un usuario

```bash
curl "http://localhost:5001/api/logs?user=alice&event=inscribió"
```

### Ver todas las clases creadas por un usuario

```bash
curl "http://localhost:5001/api/logs?user=bob&event=creada"
```

### Ver todos los usuarios inscritos en una clase específica

```bash
curl "http://localhost:5001/api/logs?class=Yoga%20Morning&event=inscribió"
```

### Ver actividad de hoy

```bash
# Obtener fecha de hoy en formato dd/MM/yyyy
# En Linux/Mac
TODAY=$(date +%d/%m/%Y)
curl "http://localhost:5001/api/logs?dateFrom=${TODAY}"

# En Windows PowerShell
$TODAY = Get-Date -Format "dd/MM/yyyy"
curl "http://localhost:5001/api/logs?dateFrom=$TODAY"
```

### Ver actividad de la última hora

```bash
# En Windows PowerShell
$dateFrom = (Get-Date).AddHours(-1).ToString("dd/MM/yyyy HH:mm")
curl "http://localhost:5001/api/logs?dateFrom=$dateFrom"
```

---

## Notas Importantes

1. **Búsqueda parcial:** Los filtros `user`, `class` y `event` hacen búsqueda parcial (case-insensitive). Por ejemplo, `user=ali` encontrará "alice", "alicia", etc.

2. **Orden de resultados:** Los resultados siempre se ordenan por fecha descendente (más recientes primero).

3. **Fechas vacías:** Si una clase no tiene nombre (string vacío), aparecerá como `"class": ""` en los resultados.

4. **Encoding de URLs:** En algunos casos, necesitarás codificar espacios en las URLs. Por ejemplo:
   - `Yoga Morning` → `Yoga%20Morning`
   - O usar comillas en curl: `curl "http://localhost:5001/api/logs?class=Yoga Morning"`

5. **Formato de fecha:** 
   - Formato simple: `dd/MM/yyyy` (ej: `25/11/2025`)
   - Formato con hora: `dd/MM/yyyy HH:mm` (ej: `25/11/2025 10:30`)
   - Nota: En URLs, los espacios deben codificarse como `%20` o usar comillas en curl

---

## Troubleshooting

### No aparecen logs

1. Verifica que RabbitMQ esté corriendo
2. Verifica que el servidor de logs esté corriendo
3. Verifica que el servidor principal esté enviando logs a RabbitMQ
4. Revisa los logs de consola del servidor principal para ver si hay errores

### Los filtros no funcionan

1. Verifica que estés usando los nombres correctos de parámetros (en inglés):
   - `user` (no `usuario`)
   - `class` (no `clase`)
   - `event` (no `evento`)
   - `dateFrom` (no `fechaDesde`)
   - `dateTo` (no `fechaHasta`)

2. Verifica el formato de fecha si usas filtros de fecha

### Error 500

Revisa los logs del servidor de logs para ver el error específico.

