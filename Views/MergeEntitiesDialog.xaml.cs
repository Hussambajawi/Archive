using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class MergeEntitiesDialog : Window
    {
        private readonly IDocumentService _documentService;
        private readonly List<CorrespondentEntity> _entities;

        public ILocalizationService Loc => LocalizationService.Instance;

        public string DialogTitle => Loc.IsArabic
            ? "برنامج ارشفة - المبرمج حسام البعجاوي | دمج وتوحيد الجهات"
            : "Archive Pro | Merge Correspondent Entities";

        public string HeaderText => Loc.IsArabic
            ? "أداة دمج وتوحيد الجهات المكررة"
            : "Duplicate Entities Merge & Consolidate Tool";

        public string SubheaderText => Loc.IsArabic
            ? "نقل وتحديث كافة المعاملات المرتبطة بجهة مكررة أو خاطئة إلى جهة رئيسية معتمدة ثم حذف الجهة المكررة تلقائياً."
            : "Reassign all documents from a duplicate entity to an official master entity and automatically remove the duplicate.";

        public string SourceLabel => Loc.IsArabic
            ? "١. الجهة المكررة أو الفرعية (المراد نقل معاملاتها ثم حذفها):"
            : "1. Duplicate Entity (To be reassigned and removed):";

        public string TargetLabel => Loc.IsArabic
            ? "٢. الجهة الرئيسية المعتمدة (المراد دمج المعاملات إليها والاحتفاظ بها):"
            : "2. Master Official Entity (To receive documents and be kept):";

        public string NoteText => Loc.IsArabic
            ? "⚠️ تنبيه: سيتم تحديث جميع المعاملات الواردة والصادرة التابعة للجهة المكررة لتصبح تابعة للجهة الرئيسية المعتمدة تلقائياً، وسيتم حذف اسم الجهة المكررة من قائمة الجهات وتسجيل العملية في سجل التدقيق."
            : "⚠️ Notice: All inbound and outbound documents associated with the duplicate entity will be updated to point to the master entity, and the duplicate entity will be permanently removed.";

        public string MergeButtonText => Loc.IsArabic
            ? "🔗 تنفيذ الدمج والتوحيد الآن"
            : "🔗 Execute Merge & Consolidate";

        public int MergedDocumentCount { get; private set; }

        public MergeEntitiesDialog(IDocumentService documentService, IEnumerable<CorrespondentEntity> entities)
        {
            InitializeComponent();
            DataContext = this;
            _documentService = documentService;
            _entities = entities?.OrderBy(e => e.DisplayName).ToList() ?? new List<CorrespondentEntity>();

            CmbSourceEntity.ItemsSource = _entities;
            CmbTargetEntity.ItemsSource = _entities;

            if (_entities.Count > 1)
            {
                CmbSourceEntity.SelectedIndex = 0;
                CmbTargetEntity.SelectedIndex = 1;
            }
        }

        private void CmbSourceEntity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSourceEntity.SelectedItem is CorrespondentEntity src)
            {
                TxtSourceInfo.Text = Loc.IsArabic
                    ? $"النوع: {src.LocalizedEntityType}  |  الملاحظات: {src.DisplayNotes}"
                    : $"Type: {src.LocalizedEntityType}  |  Notes: {src.DisplayNotes}";
            }
            else
            {
                TxtSourceInfo.Text = string.Empty;
            }
        }

        private void CmbTargetEntity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTargetEntity.SelectedItem is CorrespondentEntity tgt)
            {
                TxtTargetInfo.Text = Loc.IsArabic
                    ? $"النوع: {tgt.LocalizedEntityType}  |  الملاحظات: {tgt.DisplayNotes}"
                    : $"Type: {tgt.LocalizedEntityType}  |  Notes: {tgt.DisplayNotes}";
            }
            else
            {
                TxtTargetInfo.Text = string.Empty;
            }
        }

        private async void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            if (CmbSourceEntity.SelectedItem is not CorrespondentEntity source ||
                CmbTargetEntity.SelectedItem is not CorrespondentEntity target)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى تحديد كل من الجهة المكررة والجهة الرئيسية." : "Please select both duplicate and master entities.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (source.Id == target.Id)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "لا يمكن دمج الجهة في نفسها! يرجى اختيار جهتين مختلفتين." : "Cannot merge an entity into itself! Please choose two distinct entities.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string confirmMsg = Loc.IsArabic
                ? $"هل أنت متأكد من دمج الجهة:\n«{source.DisplayName}»\n\nفي الجهة الرئيسية:\n«{target.DisplayName}»؟\n\nسيتم تحديث كافة المعاملات المرتبطة وحذف الجهة المكررة ولا يمكن التراجع عن هذه الخطوة."
                : $"Are you sure you want to merge:\n'{source.DisplayName}'\n\ninto the master entity:\n'{target.DisplayName}'?\n\nAll linked transactions will be updated and the duplicate entity deleted.";

            string title = Loc.IsArabic ? "تأكيد دمج الجهات" : "Confirm Entity Merge";

            if (ActionPinDialog.ConfirmWarningAndVerifyPin(title, confirmMsg))
            {
                try
                {
                    int updated = await _documentService.MergeCorrespondentEntitiesAsync(source.Id, target.Id);
                    MergedDocumentCount = updated;

                    string successMsg = Loc.IsArabic
                        ? $"تمت عملية الدمج بنجاح!\nتم تحديث ({updated}) معاملة لتتبع الجهة «{target.DisplayName}»، وتم حذف الجهة المكررة."
                        : $"Merge completed successfully!\n({updated}) document(s) updated to '{target.DisplayName}', and duplicate entity removed.";

                    MessageBox.Show(successMsg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        Loc.IsArabic ? $"حدث خطأ أثناء دمج الجهات:\n{ex.Message}" : $"Error during entity merge:\n{ex.Message}",
                        Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
