namespace ScriSelfStudio.Contracts.Errors;

/// <summary>Типизированные коды ошибок (ТЗ п. 26).</summary>
public enum ErrorCode
{
    None = 0,
    Unknown = 1,

    ValidationFailed = 10,
    EntityNotFound = 11,
    EntityAlreadyExists = 12,
    UnsupportedOperation = 13,

    ProjectNotFound = 20,
    ProjectFormatInvalid = 21,
    ProjectAlreadyOpen = 22,
    ProjectNotOpen = 23,
    UnsupportedDocumentVersion = 24,

    SchemaMigrationFailed = 30,
    StorageReadFailed = 31,
    StorageWriteFailed = 32,
    BackupFailed = 33,
    RecoveryReadFailed = 34,

    EditorOperationFailed = 40,
    EditorOperationConflict = 41,

    SettingsReadFailed = 50,
    SettingsWriteFailed = 51,

    NavigationFailed = 60,
    FileOperationFailed = 70,

    CloudAuthFailed = 80,
    CloudOperationFailed = 81,
}
