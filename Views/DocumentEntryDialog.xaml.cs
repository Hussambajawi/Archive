using System.Windows;
using DocuArchivePro.Services;
using DocuArchivePro.ViewModels;

namespace DocuArchivePro.Views
{
    public partial class DocumentEntryDialog : Window
    {
        private readonly DocumentEntryViewModel _vm;

        public DocumentEntryDialog(DocumentEntryViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.RequestClose += success =>
            {
                DialogResult = success;
                Close();
            };

            vm.RequestOpenAddCategoryDialog += async () =>
            {
                var dlg = new AddCategoryDialog(_vm.DocumentService)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && dlg.CreatedCategory != null)
                {
                    await _vm.AddAndSelectCategoryAsync(dlg.CreatedCategory);
                }
            };

            vm.RequestOpenEditCategoryDialog += async cat =>
            {
                var dlg = new AddCategoryDialog(_vm.DocumentService, cat)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && dlg.CreatedCategory != null)
                {
                    await _vm.AddAndSelectCategoryAsync(dlg.CreatedCategory);
                }
            };

            vm.RequestOpenAddEntityDialog += async (targetField, currentText) =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanManageEntities)
                {
                    bool isAr = LocalizationService.Instance.IsArabic;
                    string actionTitle = isAr
                        ? "إضافة جهة صادر / وارد للمحفوظات (كلمة سر الجهات الخاصة)"
                        : "Add Saved Inbound / Outbound Entity (Entity Security Password)";

                    if (!ActionPinDialog.VerifyEntityAdditionPin(actionTitle, this))
                    {
                        return;
                    }
                }

                string defaultType = _vm.Type == Models.DocumentType.Outbound
                    ? (targetField == "Recipient" ? "Outbound" : "Both")
                    : (targetField == "Sender" ? "Inbound" : "Both");

                var dlg = new AddEntityDialog(_vm.DocumentService, initialName: currentText, defaultEntityType: defaultType)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && dlg.CreatedEntity != null)
                {
                    await _vm.AddAndSelectEntityAsync(dlg.CreatedEntity, targetField);
                }
            };

            Closed += (s, e) => _vm.CleanupTempScanFiles();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    _vm.AddDroppedPaths(files);
                }
            }
        }
    }
}
