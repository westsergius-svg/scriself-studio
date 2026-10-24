namespace ScriSelfStudio.Contracts.Errors;

/// <summary>Пользовательские сообщения по умолчанию (ТЗ п. 26: понятное сообщение и следующее действие).</summary>
public static class ErrorMessages
{
    public static string For(ErrorCode code) => code switch
    {
        ErrorCode.None => string.Empty,
        ErrorCode.ValidationFailed => "Данные не прошли проверку.",
        ErrorCode.EntityNotFound => "Элемент не найден: возможно, он был удалён.",
        ErrorCode.EntityAlreadyExists => "Элемент с таким названием уже существует.",
        ErrorCode.UnsupportedOperation => "Эта операция пока недоступна.",
        ErrorCode.ProjectNotFound => "Файл проекта не найден.",
        ErrorCode.ProjectFormatInvalid => "Файл проекта повреждён или имеет неверный формат.",
        ErrorCode.ProjectAlreadyOpen => "Проект уже открыт.",
        ErrorCode.ProjectNotOpen => "Проект не открыт.",
        ErrorCode.UnsupportedDocumentVersion => "Версия файла проекта не поддерживается этой сборкой.",
        ErrorCode.SchemaMigrationFailed => "Не удалось обновить файл проекта до текущей версии.",
        ErrorCode.StorageReadFailed => "Не удалось прочитать файл проекта.",
        ErrorCode.StorageWriteFailed => "Не удалось сохранить проект.",
        ErrorCode.BackupFailed => "Не удалось создать резервную копию. Сохранение отменено.",
        ErrorCode.RecoveryReadFailed => "Не удалось прочитать файл восстановления.",
        ErrorCode.EditorOperationFailed => "Не удалось выполнить операцию с текстом.",
        ErrorCode.EditorOperationConflict => "Текст сцены был изменён в другом месте. Откройте сцену заново.",
        ErrorCode.SettingsReadFailed => "Не удалось прочитать настройки приложения.",
        ErrorCode.SettingsWriteFailed => "Не удалось сохранить настройки приложения.",
        ErrorCode.NavigationFailed => "Не удалось открыть выбранный экран.",
        ErrorCode.FileOperationFailed => "Не удалось выполнить операцию с файлом.",
        ErrorCode.CloudAuthFailed => "Не удалось войти в Яндекс.Диск. Проверьте логин и пароль приложения.",
        ErrorCode.CloudOperationFailed => "Не удалось выполнить операцию с облаком.",
        _ => "Произошла непредвиденная ошибка.",
    };

    public static string? SuggestedActionFor(ErrorCode code) => code switch
    {
        ErrorCode.ProjectNotFound => "Проверьте путь к файлу и выберите проект заново.",
        ErrorCode.ProjectFormatInvalid => "Откройте резервную копию или создайте проект заново.",
        ErrorCode.UnsupportedDocumentVersion => "Обновите приложение до более новой версии.",
        ErrorCode.SchemaMigrationFailed => "Сохраните копию файла и обратитесь к журналу ошибок.",
        ErrorCode.StorageReadFailed => "Проверьте права доступа к файлу и повторите попытку.",
        ErrorCode.StorageWriteFailed => "Проверьте свободное место и права доступа к папке проекта.",
        ErrorCode.BackupFailed => "Освободите место на диске и повторите сохранение.",
        ErrorCode.SettingsReadFailed => "Настройки будут восстановлены со значениями по умолчанию.",
        ErrorCode.EditorOperationConflict => "Закройте и снова откройте сцену.",
        ErrorCode.EntityNotFound => "Обновите список и повторите действие.",
        ErrorCode.CloudAuthFailed => "Создайте пароль приложения в настройках Яндекса и попробуйте снова.",
        _ => null,
    };
}
