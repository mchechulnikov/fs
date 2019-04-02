# Файловая система
Библиотека, реализующая файловую систему внутри файла-носителя.<br/>
Функционал решения:
* даёт возможность создать новый файл-носитель, которые содержит в себе данные файловой системы (ФС);
* подключать и отключать ФС;
* позволяет создавать и удалять именованные файлы внутри ФС;
* осуществляет многопоточный доступ на чтение каждого из файлов;
* осуществляет доступ на запись файла.

## О решении
Данная реализация файловой системы (ФС) представляет собой файл в хост-ФС со специальной разметкой.
![Схема ФС](./img/fs-scheme.png)
Объём "диска" разбивается на *блоки* – отрезки фиксированного размера (1024 байта). Блоки нумеруются от нуля. Номер блока – это его уникальный идентификатор в ФС. Блоки нумеруются от 0.<br/>
Самый первый блок – *superblock* – содержит метаинформацию о ФС. Следом идёт группа *bitmap-блоков* – это битовая маска занятых/свободных блоков ФС. Далее вперемешку находятся блоки индексов, мета-блоки файлов и блоки данных файлов.

### Superblock
Это блок с номером 0. Содержит метаинформацию о файловой системе:
* магическое число, которое указывает на то что это действительно та ФС;
* флаг `IsDirty` – указывает на то, примонтирована ФС или нет;
* размер блока ФС;
* номер корневого блока индекса.

### Bitmap
Эти блоки служат для быстрого отслеживания свободных блоков, что необходимо для процесса аллокации блоков под записываемые данные. Если блок занят – бит установлен. Если блок свободен – бит равен 0.
![Bitmap](./img/fs-bitmap.png)

Количество bitmap-блоков определяет размер ФС. В данной реализации принят размер в `2^17` блоков на bitmap. Это обеспечивает следующий размер ФС
```
1024 байт в блоке
* 8 бит в байте
* 2^17 блоков на bitmap
= 1 073 741 824 байт 
~ 1 ГБ
```
Однако, даже такой небольшой объём требует bitmap размером в ~ 1 МБ. Искать свободный блок линейно при каждой аллокации выходит слишком дорого, поэтому поиск осуществляется не по самому bitmap'у, а по дереву отрезков следующего вида
![Bitmap tree](./img/fs-bitmap-tree.png)
Данное дерево строится in-memory после монтирования ФС. Так как это дерево содержит постоянное количество элементов, его можно держать массивом, поэтому накладных расходов на указатели нет. Эта структура обеспечивает логарифмическое время поиска и обновления.

### Индексы
Реализован один индекс: B+-дерево по именам файлов, чтобы поиск файла занимал логарифмическое время. В листьях хранятся соответствующие номера FMB.<br/>
Из-за того что ключом в дереве выступает имя файла, оно ограничено 16 символами для простоты.

### File meta blocks, FMB
Это блок, содержащий мета-информацию о файле, а именно массив direct блоков с данными и массив indirect блоков с данными.
* Direct data block – блок с данными, номер которого хранится непосредственно в FMB.
* Indirect data block – блок с номерами блоков с данными. Номера таких indirect data block'ов хранятся в FMB.
![Схема ФС](./img/fs-fmb.png)
Каждый FMB может хранить
* до 16 номеров direct-блоков;
* до 128 номеров indirect-блоков.
Это обеспечивает максимальный размер файла в
```
(
    16 direct-блоков
    + (
        128 indirect-блоков
        * 1024 байта на блок
        / 4 байта в номере блока
    )
)
* 1024 байта на блок
= 33 570 816 байт в файле
```

## О реализации
Репозиторий содержит 2 проекта:
* `Jbta.VirtualFileSystem` — библиотека, реализующая ФС.
* `Jbta.VirtualFileSystem.Tests` — набор интеграционных тестов на библиотеку.

Библиотека реализована на базе .NET Core 2.2.

### Зависимости
NuGet пакеты:
* [xUnit](https://www.nuget.org/packages/xunit/) — фреймворк для написания тестов.

Писалось, собиралось и тестировалось в JetBrains Rider 2018.3.4 на macOS Mojave 10.14.4.

### API
Для управления файловыми системами предоставлены статические методы класса `FileSystemManager`.

#### Методы `FileSystemManager`

##### Инициализация новой файловой системы
``` C#
Task Init(string volumePath)
```

###### Пример
``` C#
await FileSystemManager.Init("./foo.dat");
``` 
Будет создан файл уже размеченный том  в файле `foo.dat` готовый к монтированию.

##### Монтирование новой файловой системы
``` C#
IFileSystem Mount(string volumePath)
```

###### Пример
``` C#
var fileSystem = FileSystemManager.Mount("./foo.dat");
```
Сначала читается и валидируется заголовок файла `foo.dat`. Затем, если всё ОК, вычитываются superblock, bitmap-блоки и корневой индексный блок. В superblock ставится флаг `IsDirty` в положение `true`. Далее, над bitmap'ом в памяти строится дерево поиска, создаётся объект `fileSystem` типа `IFileSystem` и сохраняется во внутренний список примонтированных файловых систем.

##### Размонтирование файловой системы
``` C#
static void Unmount(IFileSystem fileSystem)
```

###### Пример
``` C#
FileSystemManager.Unmount(fileSystem);
```
Процесс размонтирования состоит в удалении файловой системы из списка примонтированных файловых систем, `IsDirty`ставится в положение `false`, а также ФС удаляется из памяти.

#### Объект типа `IFileSystem`

##### Создание файла
``` C#
Task<IFile> CreateFile(string fileName);
```

###### Пример
``` C#
var file = await fileSystem.CreateFile("foobar");
```
Если файл уже присутствует в индексе, будет выбрашено исключение. Если фаайл отсутствует, то сначала создаётся и записывается мета-блок файла. Затем имя и номер мета-блока файла добавляются в файловый индекс.

##### Удаление файла
``` C#
Task DeleteFile(string fileName);
```

###### Пример
``` C#
await fileSystem.DeleteFile("foobar");
```
Номер мета-блока файла ищется в файловом индексе по имени файла. Далее мета-блок файла считывается и из него извлекаются номера блоков с данными (и direct, и, посредством дополнительных чтений, из indirect). Эти блоки помечаются как свободные. Затем имя файла и номер мета-блока удаляются из индекса.

##### Открытие файла
``` C#
Task<IFile> OpenFile(string fileName);
```

###### Пример
``` C#
var file = await fileSystem.OpenFile("foobar");
```
Во время открытия файла, номер мета-блока файла ищется в индексе, затем мета-блок считывается, упаковывается в объект типа `IFile`, который сохраняется в списке открытых файлов ФС.

##### Закрытие файла
``` C#
bool CloseFile(IFile file);
```

###### Пример
``` C#
var wasFileClosed = await fileSystem.CloseFile(file);
```
Файл удаляется из списка открытых файлов ФС.

##### Путь до файла-носителя ФС
``` C#
string VolumePath { get; }
```

##### Размер ФС в байтах
``` C#
ulong VolumeSize { get; }
```

##### Используемый объём ФС в байтах
``` C#
ulong UsedSpace { get; }
```
Вычисляется с точностью до блока.

##### Свободный объём в ФС
``` C#
ulong UnusedSpace { get; }
```
Вычисляется с точностью до блока.

#### Объект типа `IFile`

##### Имя файла
``` C#
string Name { get; }
```

##### Размер файла в байтах
``` C#
int Size { get; }
```

##### Чтение контента файла
``` C#
Task<Memory<byte>> Read(int offset, int length);
```

###### Пример
``` C#
var fileData = await file.Read(5, 42);
```
Будет прочитано 42 байта начиная с 5-го по счёту байта в файле.

##### Запись контента в файл
``` C#
Task Write(int offset, byte[] data);
```

###### Пример
``` C#
var data = Encoding.Unicode.GetBytes("some data");
await file.Write(1142, data);
```
Будет записано в 18 байт (строка `some data` – 9 символов в UTF-16) начиная с 1142-го байта в файле. Если в этой позиции уже есть данные, то байты строки запишутся поверх. Если `offset` больше текущей длины файла, то будет выброшено исключение. Если во время записи окажется, что данные выходят за пределы текущей длины файла, для файла будут аллоцированы дополнительные блоки.

## Куда можно двигаться дальше?
Ниже перечислены меры, которые можно предпринять для улучшения текущего решения.
* Отказоустойчивость. Для сохранения консистентного состояния ФС после аварийного отключения потребуется реализация транзакционной системы и журналирования транзакций. Это весьма ресурсоёмкая задача.
* Возможность работы из нескольких процессов. Сейчас ФС монтируется лишь к одному процессу и безопасность работы с ней ограничена именно этим одним процессом. В реальной ситуации может потребоваться обеспечение доступа на чтение/запись файлов из нескольких процессов.
* Производительность. Здесь несколько моментов, о которых, однако, стоит говорить **только** после снятия профилей и тестов на производительность:
    * Понинизить количество обращений к файлу-носителю. Это должно привести к снижению количества дисковых операций. Для этого можно рассмотреть использовавание кэширования блоков файла/индекса/bitmap'а в память.
    * Сейчас работа с файлом закрыта RW-блокировкой, что означает, что паралелльно читать файл могут сколько угодно потоков-читателей, но писать может только один поток-писатель. Может оказаться полезным понизить гранулярность блокивки. Например, блокировать не файлами целиком, а страницами – наборами из фиксированного числа блоков. Это позволит писать "параллельно" в разные части файла.
    * Замерить время, требуемое на аллокации в основной памяти при операциях над файлами. Возможно, по некоторым горячим путям можно сократить количество аллокаций.
* Вместительность.
    * Увеличить размер битовой маски.
    * В FMB добавить double indirect и triple indirect блоки.
    * Увеличить размер номера блока до беззнакового 32- или 64-разрядного значения.
* Безопасность. Текущая реализация не зануляет блоки с данными при удалении или сокращении размера файла, только помечает блоки как свободные.
* Директории. Для этого потребуется всего лишь расширить индекс и ввести ещё один вид мета-блоков – directory meta block (DMB), который будет содержать список файлов и номера FMB соответствующих файлов.
* Атрибуты файлов.


## Дополнительно
За основу архитектуры ФС взята архитектура файловой системы [BFS](https://en.wikipedia.org/wiki/Be_File_System), описанная в книге [Practical File System Design](http://www.nobius.org/practical-file-system-design.pdf) за авторством Dominic Giampaolo.