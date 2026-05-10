# CrackHash - Distributed Hash Cracking System



## Архитектура системы CrackHash

Система состоит из двух типов сервисов: Manager (менеджер) и Worker (воркер). Взаимодействие между ними осуществляется по HTTP внутри Docker-сети.
Manager (Менеджер)

### Менеджер — центральный компонент системы, который:

- Принимает REST-запросы от клиента на взлом хэша

- Сохраняет состояние задачи в MongoDB 

- Разбивает общее пространство перебора на подзадачи и публикует их в очередь RabbitMQ

- Принимает результаты от воркеров через очередь RabbitMQ

- Агрегирует прогресс и формирует итоговый ответ

- Отслеживает зависшие подзадачи (таймауты) и переотправляет их

- При рестарте восстанавливает незавершенные задачи из MongoDB

### Worker (Воркер)

Воркер — вычислительный узел, который:

- При запуске подписывается на очередь задач RabbitMQ

- Получает задачу: диапазон индексов, хэш, максимальную длину слова

- Генерирует слова итеративно, вычисляет MD5, сравнивает с целевым

- Отправляет промежуточные и финальные результаты в очередь результатов RabbitMQ

- При обнаружении стоп-слова имитирует критическую ошибку


### Схема взаимодействия компонентов
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                  CLIENT                                     │
│                                                                             │
│    POST /api/hash/crack         GET /api/hash/status?crackId=<UUID>         │
│    {hash, maxLength}            → {status, progress, data}                  │
└─────────────────────────────────────┬───────────────────────────────────────┘
                                      │
                                      │ HTTP (REST API)
                                      │
┌─────────────────────────────────────▼───────────────────────────────────────┐
│                           MANAGER SERVICE (Port 8080)                       │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐  │
│  │ Task Management │  │ MongoRepository │  │ Fault Tolerance             │  │
│  │ • Create tasks  │  │ • Save state    │  │ • Restore pending tasks     │  │
│  │ • Split ranges  │  │ • Update        │  │ • Retry timed-out subtasks  │  │
│  │ • Aggregate     │  │   progress      │  │ • Mark ERROR on max retries │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────────┘  │
└───┬──────────────────┬──────────────────────────────┬───────────────────────┘
    │                  │                              │
    │ Publish tasks    │ Consume results              │ Read/Write state
    ▼                  │                              │
┌──────────────────────▼──────────┐    ┌──────────────▼─────────────────────┐
│         RABBITMQ                │    │      MongoDB Replica Set           │
│  ┌─────────────────────────────┐│    │  ┌────────────┐ ┌────────────┐     │
│  │ crack_tasks (queue)         ││    │  │  Primary   │ │ Secondary  │     │
│  │ → DLQ: crack_tasks_dlq      ││    │  │  (write)   │ │  (read)    │     │
│  └─────────────────────────────┘│    │  └────────────┘ └────────────┘     │
│  ┌─────────────────────────────┐│    │         ┌────────────┐             │
│  │ crack_results (queue)       ││    │         │ Secondary  │             │
│  └─────────────────────────────┘│    │         │  (read)    │             │
└───────────────────────┬─────────┘    └────────────────────────────────────┘
    │ Consume tasks     │ Publish results
    ▼                   │
┌───────────────────────────────────────────────────────────────────────────────┐
│                          WORKER SERVICES (1..N)                               │
│   ┌─────────────────────┐   ┌─────────────────────┐   ┌───────────────────┐   │
│   │     Worker 1        │   │     Worker 2        │   │    Worker 3       │   │
│   │  Range: [0, N/3)    │   │  Range: [N/3, 2N/3) │   │  Range: [2N/3, N] │   │
│   │  • Word generation  │   │  • Word generation  │   │  • Word generation│   │
│   │  • MD5 hashing      │   │  • MD5 hashing      │   │  • MD5 hashing    │   │
│   │  • Stop-word check  │   │  • Stop-word check  │   │  • Stop-word check│   │
│   └─────────────────────┘   └─────────────────────┘   └───────────────────┘   │
└───────────────────────────────────────────────────────────────────────────────┘
```

## Отказоустойчивость

### Сохранность данных при отказе менеджера
- Все задачи и прогресс хранятся в MongoDB (реплицируемой)
- При рестарте менеджер восстанавливает незавершtнные задачи из БД
- Необработанные результаты из очереди не теряются

### Отказоустойчивость MongoDB
- Настроен Replica Set: 1 Primary + 2 Secondary
- Write Concern: Majority — запись подтверждается большинством нод
- Read Preference: SecondaryPreferred — чтение с secondary для снижения нагрузки
- При отказе Primary автоматически выбирается новый Primary

### Сохранность данных при отказе воркера
- Очередь RabbitMQ с подтверждениями (ack)
- Если воркер не ответил за TASK_TIMEOUT_MIN, подзадача переотправляется другому воркеру
- После 3 неудачных попыток задача помечается как ERROR

### Сохранность данных при отказе RabbitMQ
- Все сообщения персистентные (Persistent + DeliveryMode=2)
- При рестарте RabbitMQ сообщения восстанавливаются
- Менеджер сохраняет подзадачи в БД до публикации в очередь

### Dead Letter Queue
- Стоп-слово (STOP_WORD) при обнаружении вызывает исключение в воркере
- Сообщение после ошибки перемещается в crack_tasks_dlq
- Администратор может просмотреть DLQ через GET /api/hash/dlq


## Описание API

### External API (Manager) - для клиентов
Этот интерфейс предназначен для внешних клиентов, желающих отправить задание на взлом хэша или проверить его статус.

#### POST /api/hash/crack

Создание запроса на взлом хэша.

**Request:**

```json
{
  "hash": "e2fc714c4727ee9395f324cd2e7f331f",
  "maxLength": 4
}
```

**Response (200 OK):**

```json
{
  "requestId": "0160c0ac-5c32-4145-ac08-0ff3f9042401"
}
```

**Status Codes:**

- `200 OK` - запрос принят
- `400 Bad Request` - неверный формат запроса
- `500 Internal Server Error` - нет доступных воркеров или внутренняя ошибка

---

#### GET /api/hash/status

Получение статуса выполнения запроса.

**Parameters:**

- `crackId` - UUID запроса, полученный при создании

**Response (IN_PROGRESS):**

```json
{
  "status": "IN_PROGRESS",
  "progress": 65,
  "data": null
}
```

**Response (READY):**

```json
{
  "status": "READY",
  "progress": 100,
  "data": [
    "abcd"
  ]
}
```

**Response (ERROR):**

```json
{
  "status": "ERROR",
  "progress": 50,
  "data": null
}
```
---
#### GET /api/hash/dlq

Просмотр сообщений в Dead Letter Queue (для администратора).

**Response (200 OK):**
```json
{
  "count": 1,
  "messages": [
    {
      "deliveryTag": 1,
      "content": "{\"RequestId\":\"...\",\"SubTaskId\":\"...\",...}",
      "reason": "Stop-word encountered",
      "receivedAt": "2026-05-10T15:30:00Z"
    }
  ]
}
```
---

## Инструкция по запуску

### Предварительные требования
- установленные  Docker и Docker Compose


### Запуск системы в Docker
1. Настройка Docker

```
cd lab1/

# Добавить пользователя в группу docker
sudo usermod -aG docker $USER

# Применить изменения группы (или перелогиниться)
newgrp docker

# Проверить, что Docker работает
docker ps
```


2. Сборка и запуск контейнеров
```
# Остановить текущие контейнеры (если есть)
docker compose down

# Пересобрать образы
docker compose build --no-cache

# Запустить контейнеры
docker compose up
# или с сохранением логов в файл и выводом к консоль
docker compose up 2>&1 | tee logs.txt
```




#### Посмотреть через Swagger
если программа запущена в docker
[для воркера](http://localhost:8081/swagger/index.html) 
[для менеджера](http://localhost:8080/swagger/index.html) 




## Тестирование отказоустойчивости
### Сценарий 1. Остановка менеджера

```
curl -X POST http://localhost:8080/api/hash/crack \
  -H "Content-Type: application/json" \
  -d '{"hash":"912ec803b2ce49e4a541068d495ab570", "maxLength":4}' && \
docker compose stop manager

# Смотри логи воркеров – они завершат вычисления.
docker-compose start manager -d

# Ждем {"status":"READY","progress":100,"data":["asdf"]}

curl "http://localhost:8080/api/hash/status?crackId="
```

---

### Сценарий 2. Отказ primary MongoDB
```
# 
curl -X POST http://localhost:8080/api/hash/crack \
  -H "Content-Type: application/json" \
  -d '{"hash":"040b7cf4a55014e185813e0644502ea9", "maxLength":5}' && \
docker compose stop mongodb-primary

# как проверить, какая нода primary?
docker exec -it lab2-mongodb-secondary-1-1 mongosh --eval "rs.status().members.map(m => ({name: m.name, state: m.stateStr}))"

# будет что то вроде
# [
#   { name: 'mongodb-primary:27017', state: 'SECONDARY' },
#   { name: 'mongodb-secondary-1:27017', state: 'PRIMARY' },
#   { name: 'mongodb-secondary-2:27017', state: 'SECONDARY' }
# ]



# Ждем ~30 секунд. Проверяем статус задачу – должно быть IN_PROGRESS или READY

curl "http://localhost:8080/api/hash/status?crackId="

docker compose start mongodb-primary

```

---

### Сценарий 3. Стоп RabbitMQ
```
# останавливаем rabbitmq
docker compose stop rabbitmq

# создаем задачу
curl -X POST http://localhost:8080/api/hash/crack \
-H "Content-Type: application/json" \
-d '{"hash":"040b7cf4a55014e185813e0644502ea9", "maxLength":5}'

# проверяем - задача создана, но процесс нулеовй
curl "http://localhost:8080/api/hash/status?crackId="


# восстанавливаем rabbitmq
docker-compose start rabbitmq


# Через CHECK_INTERVAL_SEC / TASK_TIMEOUT_SEC секунд TaskTimeoutService вызовет RetryPendingPublishes и отправит задачи
```

---


### Сценарий 4. Остановка воркера
```
#
curl -X POST http://localhost:8080/api/hash/crack \
  -H "Content-Type: application/json" \
  -d '{"hash":"9e9d7a08e048e9d604b79460b54969c3", "maxLength":5}'

# Когда воркер начнёт обработку, остановить его: 

   
docker compose stop worker1

# 
curl "http://localhost:8080/api/hash/status?crackId="



# Ждем TASK_TIMEOUT_MIN=1 
# (это ставим в docker-compose.yml).
# Логи менеджера должны показать таймаут и переотправку.

# Задача должна завершиться (READY).
curl "http://localhost:8080/api/hash/status?crackId="
```

---


### Сценарий 5. Stop-word и DLQ
```
# Создай ID6 с хешем bom и maxLength=3.

#Воркер, получивший подзадачу с bom, выбросит исключение, сообщение уйдёт в DLQ. Менеджер через 
#таймаут зафиксирует подвисшую подзадачу, инкрементирует RetryCount и переотправит. После 3-х 
#таймаутов (RetryCount >= 3) CheckTimedOutSubtasks пометит задачу как ERROR.

# Проверяем статус
curl "http://localhost:8080/api/hash/status?crackId="

# Провеяем DLQ: curl http://localhost:8080/api/hash/dlq – должны быть сообщения.
```
