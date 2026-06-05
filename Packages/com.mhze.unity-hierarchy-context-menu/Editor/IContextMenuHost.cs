namespace mhze.HierarchyContextMenu
{
    internal interface IContextMenuHost
    {
        void CancelSubmenuSchedule();
        void ScheduleHideSubmenu();
        void Close();
    }
}
