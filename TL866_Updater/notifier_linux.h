#ifndef NOTIFIER_H
#define NOTIFIER_H

#include <QWidget>
#include <QSocketNotifier>

class Notifier : public QWidget
{
    Q_OBJECT
public:
    explicit Notifier();

 ~Notifier();
signals:
    void deviceChange(bool arrived);

public slots:

private slots:
        void udev_event();

private:
    QSocketNotifier *socket_notifier;
    void RegisterUsbNotifications();
};

#endif // NOTIFIER_H
