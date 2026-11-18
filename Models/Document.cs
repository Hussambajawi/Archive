using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocuArchivePro.Models
{
    public class Document
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string RegistrationNumber { get; set; } = string.Empty; // رقم القيد / الإشارة

        public DocumentType Type { get; set; } = DocumentType.Inbound; // صادر أو وارد

        [Required]
        public DateTime RegistrationDate { get; set; } = DateTime.Now; // تاريخ التسجيل / القيد

        public DateTime DocumentDate { get; set; } = DateTime.Now; // تاريخ الخطاب / الوثيقة الأصلية

        [Required]
        [MaxLength(300)]
        public string Subject { get; set; } = string.Empty; // الموضوع

        [Required]
        [MaxLength(200)]
        public string Sender { get; set; } = string.Empty; // الجهة الصادر منها / الراسل

        [Required]
        [MaxLength(200)]
        public string Recipient { get; set; } = string.Empty; // الجهة الموجه إليها / المستلم

        public int? CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public virtual Category? Category { get; set; }

        public DocumentPriority Priority { get; set; } = DocumentPriority.Normal; // الأولوية

        public DocumentStatus Status { get; set; } = DocumentStatus.New; // الحالة

        [MaxLength(100)]
        public string DeliveryMethod { get; set; } = "Hand Delivery"; // طريقة الاستلام أو الإرسال

        [MaxLength(100)]
        public string ExternalReferenceNumber { get; set; } = string.Empty; // رقم إشارة خارجي

        public DateTime? FollowUpDate { get; set; } // تاريخ المتابعة / الاستحقاق

        [MaxLength(2000)]
        public string Notes { get; set; } = string.Empty; // تفاصيل وملاحظات

        [MaxLength(200)]
        public string Department { get; set; } = string.Empty; // الإدارة / القسم المعني

        [MaxLength(200)]
        public string Tags { get; set; } = string.Empty; // وسوم للبحث السريع

        [MaxLength(50)]
        public string RelatedRegistrationNumber { get; set; } = string.Empty; // رقم قيد المعاملة المرتبطة (صادر / وارد)

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public virtual ICollection<DocumentAttachment> Attachments { get; set; } = new List<DocumentAttachment>();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
