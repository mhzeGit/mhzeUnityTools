namespace Gigaduck.HierarchyContextMenu
{
    internal interface IContextMenuHost
    {
        void CancelSubmenuSchedule();
        void ScheduleHideSubmenu();
        void Close();
    }
}
