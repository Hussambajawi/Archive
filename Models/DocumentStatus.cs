namespace DocuArchivePro.Models
{
    public enum DocumentStatus
    {
        New = 1,          // جديد
        InProgress = 2,   // قيد المعالجة / المتابعة
        Completed = 3,    // منجز / تمت المعالجة
        Archived = 4,     // مؤرشف / محفوظ نهائياً
        Cancelled = 5     // ملغى
    }
}
