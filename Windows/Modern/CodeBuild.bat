@echo off
%SystemRoot%\System32\chcp.com 65001 >nul
setlocal enabledelayedexpansion

:: Имя итогового файла
set "outputFile=Result_Merged.txt"

:: Удаляем старый итоговый файл, если он уже существует
if exist "%outputFile%" del "%outputFile%"

set /a counter=1

:: Используем команду 'dir /b /a-d', которая выдает только файлы (без папок)
for /f "delims=" %%f in ('dir /b /a-d') do (
    
    :: Проверяем, что это не сам .bat файл и не итоговый файл
    if /I not "%%f"=="%~nx0" (
        if /I not "%%f"=="%outputFile%" (
            
            :: Записываем номер и имя файла в результат
            echo !counter!. %%f>> "%outputFile%"
            
            :: Копируем содержимое файла в результат
            type "%%f">> "%outputFile%"
            
            :: Добавляем пустую строку-разделитель
            echo.>> "%outputFile%"
            
            set /a counter+=1
        )
    )
)

echo Готово! Все файлы успешно объединены в %outputFile%.
pause